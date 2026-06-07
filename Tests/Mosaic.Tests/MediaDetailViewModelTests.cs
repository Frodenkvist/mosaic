using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;
using Mosaic.ViewModels;

namespace Mosaic.Tests;

/// <summary>
/// Per-episode edit/remove from the series detail view: correcting a mis-parsed season/episode
/// number or title, and removing an individual episode (without touching the file on disk).
/// </summary>
public class MediaDetailViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_mdvm_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mosaic_mdvmdir_{Guid.NewGuid():N}");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly AppPaths _paths;

    public MediaDetailViewModelTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var db = new MosaicDbContext(_options);
        db.Database.EnsureCreated();
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    private MediaLibrary NewLibrary() =>
        new(new TestDbContextFactory(_options), _paths, new NoOpMediaArtworkService());

    private MediaDetailViewModel NewDetailVm(MediaLibrary library, IDialogService dialogs) =>
        new(library, new StubMediaPlaybackTracker(), new NoOpMediaArtworkService(), dialogs);

    private async Task<int> SeedSeriesAsync(params (int Season, int Episode, string Title)[] episodes)
    {
        await using var db = new MosaicDbContext(_options);
        var series = new MediaItem
        {
            Kind = MediaKind.Series, Title = "Death Note", FolderPath = _root, DateAdded = DateTimeOffset.UtcNow,
        };
        foreach (var (season, episode, title) in episodes)
            series.Episodes.Add(new MediaItem
            {
                Kind = MediaKind.Episode,
                Title = title,
                FilePath = Path.Combine(_root, $"{season}-{episode}-{title}.mkv"),
                FolderPath = _root,
                SeasonNumber = season,
                EpisodeNumber = episode,
                DateAdded = DateTimeOffset.UtcNow,
            });
        db.MediaItems.Add(series);
        await db.SaveChangesAsync();
        return series.Id;
    }

    [Fact]
    public async Task SaveEpisode_PersistsCorrectedSeasonEpisodeAndTitle()
    {
        var library = NewLibrary();
        var seriesId = await SeedSeriesAsync((2, 9, "S02E09"));

        var vm = NewDetailVm(library, new RecordingDialogService());
        await vm.InitializeAsync(seriesId);
        var row = vm.Seasons.SelectMany(s => s.Episodes).Single();

        row.EditSeason = "2";
        row.EditEpisode = "1"; // 9th overall, but really the 1st of season 2
        row.EditTitle = "Rebirth";
        await vm.SaveEpisodeCommand.ExecuteAsync(row);

        var ep = Assert.Single(await library.GetEpisodesAsync(seriesId));
        Assert.Equal(2, ep.SeasonNumber);
        Assert.Equal(1, ep.EpisodeNumber);
        Assert.Equal("Rebirth", ep.Title);
    }

    [Fact]
    public async Task SaveEpisode_InvalidEpisodeNumber_ShowsMessage_AndDoesNotPersist()
    {
        var library = NewLibrary();
        var seriesId = await SeedSeriesAsync((2, 9, "S02E09"));
        var dialogs = new RecordingDialogService();

        var vm = NewDetailVm(library, dialogs);
        await vm.InitializeAsync(seriesId);
        var row = vm.Seasons.SelectMany(s => s.Episodes).Single();

        row.EditSeason = "2";
        row.EditEpisode = "not-a-number";
        row.EditTitle = "Whatever";
        await vm.SaveEpisodeCommand.ExecuteAsync(row);

        Assert.Equal(1, dialogs.MessageCount); // validation surfaced to the user
        var ep = Assert.Single(await library.GetEpisodesAsync(seriesId));
        Assert.Equal(9, ep.EpisodeNumber);   // unchanged
        Assert.Equal("S02E09", ep.Title);
    }

    [Fact]
    public async Task RemoveEpisode_Confirmed_RemovesOnlyThatEpisode()
    {
        var library = NewLibrary();
        var seriesId = await SeedSeriesAsync((1, 1, "S01E01"), (1, 2, "S01E02"));

        var vm = NewDetailVm(library, new RecordingDialogService(confirmResult: true));
        await vm.InitializeAsync(seriesId);
        var first = vm.Seasons.SelectMany(s => s.Episodes).First();
        var removedId = first.EpisodeId;
        await vm.RemoveEpisodeCommand.ExecuteAsync(first);

        var remaining = await library.GetEpisodesAsync(seriesId);
        Assert.Single(remaining);
        Assert.DoesNotContain(remaining, e => e.Id == removedId);
    }

    [Fact]
    public async Task RemoveEpisode_Declined_KeepsEpisode()
    {
        var library = NewLibrary();
        var seriesId = await SeedSeriesAsync((1, 1, "S01E01"), (1, 2, "S01E02"));

        var vm = NewDetailVm(library, new RecordingDialogService(confirmResult: false));
        await vm.InitializeAsync(seriesId);
        var first = vm.Seasons.SelectMany(s => s.Episodes).First();
        await vm.RemoveEpisodeCommand.ExecuteAsync(first);

        Assert.Equal(2, (await library.GetEpisodesAsync(seriesId)).Count);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Media playback tracker stub: the detail VM only reads the resume episode on load.</summary>
    private sealed class StubMediaPlaybackTracker : IMediaPlaybackTracker
    {
        public event EventHandler<int>? WatchStarted { add { } remove { } }
        public event EventHandler<int>? WatchStateChanged { add { } remove { } }
        public Task<bool> PlayAsync(int mediaItemId) => Task.FromResult(true);
        public Task SetWatchedAsync(int mediaItemId, bool watched) => Task.CompletedTask;
        public Task<MediaItem?> MarkWatchedAndAdvanceAsync(int episodeId) => Task.FromResult<MediaItem?>(null);
        public Task<MediaItem?> GetResumeEpisodeAsync(int seriesId) => Task.FromResult<MediaItem?>(null);
    }

    /// <summary>Dialog stub that records confirmations/messages and returns a fixed Confirm result.</summary>
    private sealed class RecordingDialogService : IDialogService
    {
        private readonly bool _confirmResult;
        public RecordingDialogService(bool confirmResult = true) => _confirmResult = confirmResult;

        public int ConfirmCount { get; private set; }
        public int MessageCount { get; private set; }

        public bool Confirm(string message, string title) { ConfirmCount++; return _confirmResult; }
        public void ShowMessage(string message, string title) => MessageCount++;

        public string? PickExecutable() => null;
        public string? PickFolder() => null;
        public string? PickImage() => null;
        public AddGameRequest? ShowAddGame() => null;
        public IReadOnlyList<ScanCandidate>? ShowScanResults(IReadOnlyList<ScanCandidate> candidates) => null;
        public IReadOnlyList<MediaScanCandidate>? ShowMediaScanResults(IReadOnlyList<MediaScanCandidate> candidates) => null;
        public void ShowGameDetail(int gameId) { }
        public void ShowMediaDetail(int mediaItemId) { }
    }
}
