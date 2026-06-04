using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>No-op media artwork service for tests that don't exercise fetching.</summary>
internal sealed class NoOpMediaArtworkService : IMediaArtworkService
{
    public event EventHandler<int>? MediaArtworkUpdated { add { } remove { } }
    public event EventHandler<int>? MediaArtworkFetchStarted { add { } remove { } }
    public event EventHandler<int>? MediaArtworkFetchFailed { add { } remove { } }
    public Task FetchArtworkAsync(int mediaItemId, bool refetch = false, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task<string> SetManualOverrideAsync(int mediaItemId, MediaArtworkKind kind, string sourceImagePath) =>
        Task.FromResult(sourceImagePath);
    public Task FetchMissingForAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Settings stub for media tests (TMDB key + preferred player path).</summary>
internal sealed class MediaSettingsStub : ISettingsService
{
    private readonly AppSettings _settings;
    public MediaSettingsStub(string? tmdbApiKey = null, string? preferredMediaPlayerPath = null) =>
        _settings = new AppSettings { TmdbApiKey = tmdbApiKey, PreferredMediaPlayerPath = preferredMediaPlayerPath };
    public AppSettings Current => _settings;
    public event EventHandler? Changed { add { } remove { } }
    public Task SaveAsync() => Task.CompletedTask;
}

public class MediaLibraryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_media_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mosaic_mediadir_{Guid.NewGuid():N}");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly AppPaths _paths;

    public MediaLibraryTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var db = new MosaicDbContext(_options);
        db.Database.EnsureCreated();
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    private MediaLibrary NewLibrary() =>
        new(new TestDbContextFactory(_options), _paths, new NoOpMediaArtworkService());

    [Fact]
    public async Task Scan_RecursesSubfolders_ClassifiesMoviesAndEpisodes_AndExcludesJunk()
    {
        var scan = Path.Combine(Path.GetTempPath(), $"mosaic_scanroot_{Guid.NewGuid():N}");
        try
        {
            Write(@"Movies\Inception (2010)\Inception.1080p.BluRay.x264.mkv");
            Write(@"Movies\Old Movie.mkv");
            Write(@"Movies\Extras\featurette.mkv");          // junk: extras folder + featurette name
            Write(@"TV\The Office\Season 1\The.Office.S01E01.mkv");
            Write(@"TV\The Office\Season 1\The.Office.S01E02.mkv");
            Write(@"TV\The Office\Season 1\sample.mkv");      // junk: sample
            Write(@"TV\The Office\readme.txt");               // non-video ignored

            var candidates = await NewLibrary().ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0);

            var movies = candidates.Where(c => c.Kind == MediaCandidateKind.Movie).ToList();
            var episodes = candidates.Where(c => c.Kind == MediaCandidateKind.Episode).ToList();

            Assert.Equal(2, movies.Count);
            Assert.Contains(movies, m => m.Title == "Inception" && m.Year == 2010);
            Assert.Contains(movies, m => m.Title == "Old Movie");

            Assert.Equal(2, episodes.Count);
            Assert.All(episodes, e => Assert.Equal("The Office", e.SeriesTitle));
            Assert.Contains(episodes, e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
            Assert.Contains(episodes, e => e.SeasonNumber == 1 && e.EpisodeNumber == 2);

            Assert.DoesNotContain(candidates, c =>
                c.FilePath.Contains("sample", StringComparison.OrdinalIgnoreCase) ||
                c.FilePath.Contains("featurette", StringComparison.OrdinalIgnoreCase) ||
                c.FilePath.Contains("Extras", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(scan, recursive: true);
        }

        void Write(string relative)
        {
            var full = Path.Combine(scan, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[1024]);
        }
    }

    [Fact]
    public async Task AddConfirmed_GroupsEpisodesUnderSeries_AndSkipsAlreadyKnown()
    {
        var scan = Path.Combine(Path.GetTempPath(), $"mosaic_scanroot_{Guid.NewGuid():N}");
        try
        {
            Write(@"The Office\Season 1\The.Office.S01E01.mkv");
            Write(@"The Office\Season 1\The.Office.S01E02.mkv");

            var library = NewLibrary();
            var candidates = await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0);
            await library.AddConfirmedAsync(candidates);

            var top = await library.GetLibraryAsync();
            var series = Assert.Single(top);
            Assert.True(series.IsSeries);
            Assert.Equal("The Office", series.Item.Title);
            Assert.Equal(2, series.TotalEpisodes);

            var episodes = await library.GetEpisodesAsync(series.Item.Id);
            Assert.Equal(2, episodes.Count);
            Assert.Equal(1, episodes[0].EpisodeNumber);
            Assert.Equal(2, episodes[1].EpisodeNumber);

            // Re-scanning skips files already in the library.
            var rescan = await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0);
            Assert.Empty(rescan);
        }
        finally
        {
            Directory.Delete(scan, recursive: true);
        }

        void Write(string relative)
        {
            var full = Path.Combine(scan, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[1024]);
        }
    }

    [Fact]
    public async Task Remove_CascadesEpisodesSessionsArtwork_DeletesCachedImages_KeepsVideoFiles()
    {
        // Two episode files on disk that must survive removal.
        var videoDir = Path.Combine(_root, "show");
        Directory.CreateDirectory(videoDir);
        var ep1File = Path.Combine(videoDir, "e1.mkv");
        var ep2File = Path.Combine(videoDir, "e2.mkv");
        File.WriteAllBytes(ep1File, new byte[10]);
        File.WriteAllBytes(ep2File, new byte[10]);

        // A cached poster inside the media-artwork dir that must be deleted.
        var posterFile = Path.Combine(_paths.MediaArtworkDirectory, "series_poster.jpg");
        await File.WriteAllBytesAsync(posterFile, new byte[] { 1, 2, 3 });

        int seriesId;
        await using (var db = new MosaicDbContext(_options))
        {
            var series = new MediaItem { Kind = MediaKind.Series, Title = "Show", FolderPath = videoDir, DateAdded = DateTimeOffset.UtcNow };
            series.Artwork.Add(new MediaArtwork { Kind = MediaArtworkKind.Poster, LocalPath = posterFile });
            series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "E1", FilePath = ep1File, FolderPath = videoDir, SeasonNumber = 1, EpisodeNumber = 1, DateAdded = DateTimeOffset.UtcNow });
            series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "E2", FilePath = ep2File, FolderPath = videoDir, SeasonNumber = 1, EpisodeNumber = 2, DateAdded = DateTimeOffset.UtcNow });
            db.MediaItems.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
            db.WatchSessions.Add(new WatchSession { MediaItemId = series.Episodes[0].Id, StartedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await NewLibrary().RemoveAsync(seriesId);

        await using (var db = new MosaicDbContext(_options))
        {
            Assert.Empty(await db.MediaItems.ToListAsync());
            Assert.Empty(await db.MediaArtwork.ToListAsync());
            Assert.Empty(await db.WatchSessions.ToListAsync());
        }
        Assert.False(File.Exists(posterFile), "cached poster should be deleted");
        Assert.True(File.Exists(ep1File), "the user's video files must NOT be deleted");
        Assert.True(File.Exists(ep2File), "the user's video files must NOT be deleted");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
