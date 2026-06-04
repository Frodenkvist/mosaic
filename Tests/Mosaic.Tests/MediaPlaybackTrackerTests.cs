using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

public class MediaPlaybackTrackerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_play_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mosaic_playdir_{Guid.NewGuid():N}");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly TestDbContextFactory _factory;
    private readonly AppPaths _paths;

    public MediaPlaybackTrackerTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _factory = new TestDbContextFactory(_options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    private MediaPlaybackTracker NewTracker() => new(_factory, new MediaSettingsStub());

    // --- ResolveLaunch (9.3c) ---

    [Fact]
    public void ResolveLaunch_WithConfiguredPlayer_LaunchesPlayerWithQuotedFile()
    {
        var psi = MediaPlaybackTracker.ResolveLaunch(@"D:\Films\Movie.mkv", @"C:\Program Files\VLC\vlc.exe");
        Assert.Equal(@"C:\Program Files\VLC\vlc.exe", psi.FileName);
        Assert.Equal("\"D:\\Films\\Movie.mkv\"", psi.Arguments);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void ResolveLaunch_WithoutPlayer_UsesDefaultAssociation()
    {
        var psi = MediaPlaybackTracker.ResolveLaunch(@"D:\Films\Movie.mkv", null);
        Assert.Equal(@"D:\Films\Movie.mkv", psi.FileName);
        Assert.True(psi.UseShellExecute);
        Assert.True(string.IsNullOrEmpty(psi.Arguments));
    }

    // --- Watched state & resume (9.3) ---

    [Fact]
    public async Task SetWatched_DrivesSeriesResumeToNextUnwatchedEpisode()
    {
        var (seriesId, ep) = await SeedSeriesAsync(3);
        var tracker = NewTracker();

        Assert.Equal(ep[0], (await tracker.GetResumeEpisodeAsync(seriesId))!.Id);

        await tracker.SetWatchedAsync(ep[0], true);
        Assert.Equal(ep[1], (await tracker.GetResumeEpisodeAsync(seriesId))!.Id);

        await tracker.SetWatchedAsync(ep[1], true);
        Assert.Equal(ep[2], (await tracker.GetResumeEpisodeAsync(seriesId))!.Id);

        await tracker.SetWatchedAsync(ep[2], true);
        Assert.Null(await tracker.GetResumeEpisodeAsync(seriesId)); // fully watched
    }

    [Fact]
    public async Task MarkWatchedAndAdvance_MarksWatched_AndReturnsNextEpisode()
    {
        var (_, ep) = await SeedSeriesAsync(3);
        var tracker = NewTracker();

        var next = await tracker.MarkWatchedAndAdvanceAsync(ep[0]);
        Assert.Equal(ep[1], next!.Id);
        Assert.True((await GetItemAsync(ep[0])).IsWatched);
    }

    [Fact]
    public async Task SetWatched_Clear_PersistsAndResetsResume()
    {
        var (seriesId, ep) = await SeedSeriesAsync(2);
        var tracker = NewTracker();

        await tracker.SetWatchedAsync(ep[0], true);
        await tracker.SetWatchedAsync(ep[0], false);

        // Persisted across a fresh context, and the resume falls back to the first episode.
        Assert.False((await GetItemAsync(ep[0])).IsWatched);
        Assert.Equal(ep[0], (await tracker.GetResumeEpisodeAsync(seriesId))!.Id);
    }

    // --- Tier-1 auto detection application (9.3 / 5b.3) ---

    [Fact]
    public async Task ApplyObservedPosition_AtThreshold_AutoMarksWatched()
    {
        var movieId = await SeedMovieAsync("Inception");
        await NewTracker().ApplyObservedPositionAsync(movieId, positionSeconds: 95, endTimeSeconds: 100);
        Assert.True((await GetItemAsync(movieId)).IsWatched);
    }

    [Fact]
    public async Task ApplyObservedPosition_BelowThreshold_RecordsResume_NotWatched()
    {
        var movieId = await SeedMovieAsync("Half Watched");
        await NewTracker().ApplyObservedPositionAsync(movieId, positionSeconds: 30, endTimeSeconds: 100);

        var movie = await GetItemAsync(movieId);
        Assert.False(movie.IsWatched);
        Assert.Equal(30, movie.ResumePositionSeconds);
    }

    [Fact]
    public async Task ApplyObservedPosition_NeverClearsAnAlreadyWatchedItem()
    {
        var movieId = await SeedMovieAsync("Already Done");
        var tracker = NewTracker();
        await tracker.SetWatchedAsync(movieId, true);

        await tracker.ApplyObservedPositionAsync(movieId, positionSeconds: 10, endTimeSeconds: 100);
        Assert.True((await GetItemAsync(movieId)).IsWatched);
    }

    // --- Recently-watched ordering from sessions (9.3) ---

    [Fact]
    public async Task RecentlyWatched_OrdersByMostRecentWatchActivity()
    {
        var older = await SeedMovieAsync("Older");
        var newer = await SeedMovieAsync("Newer");
        await using (var db = _factory.CreateDbContext())
        {
            db.WatchSessions.Add(new WatchSession { MediaItemId = older, StartedAt = DateTimeOffset.UtcNow.AddHours(-2) });
            db.WatchSessions.Add(new WatchSession { MediaItemId = newer, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
            await db.SaveChangesAsync();
        }

        var library = new MediaLibrary(_factory, _paths, new NoOpMediaArtworkService());
        var recent = await library.GetRecentlyWatchedAsync();

        Assert.Equal(2, recent.Count);
        Assert.Equal(newer, recent[0].Item.Id);
        Assert.Equal(older, recent[1].Item.Id);
    }

    // --- helpers ---

    private async Task<(int SeriesId, int[] EpisodeIds)> SeedSeriesAsync(int count)
    {
        await using var db = _factory.CreateDbContext();
        var series = new MediaItem { Kind = MediaKind.Series, Title = "Series", FolderPath = _root, DateAdded = DateTimeOffset.UtcNow };
        for (var i = 1; i <= count; i++)
            series.Episodes.Add(new MediaItem
            {
                Kind = MediaKind.Episode,
                Title = $"E{i}",
                FilePath = Path.Combine(_root, $"e{i}.mkv"),
                FolderPath = _root,
                SeasonNumber = 1,
                EpisodeNumber = i,
                DateAdded = DateTimeOffset.UtcNow,
            });
        db.MediaItems.Add(series);
        await db.SaveChangesAsync();
        return (series.Id, series.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => e.Id).ToArray());
    }

    private async Task<int> SeedMovieAsync(string title)
    {
        await using var db = _factory.CreateDbContext();
        var movie = new MediaItem { Kind = MediaKind.Movie, Title = title, FilePath = Path.Combine(_root, $"{title}.mkv"), FolderPath = _root, DateAdded = DateTimeOffset.UtcNow };
        db.MediaItems.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private async Task<MediaItem> GetItemAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.MediaItems.AsNoTracking().FirstAsync(m => m.Id == id);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
