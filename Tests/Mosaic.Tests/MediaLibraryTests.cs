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

    private static string NewScanRoot() =>
        Path.Combine(Path.GetTempPath(), $"mosaic_scanroot_{Guid.NewGuid():N}");

    private static void WriteFile(string scanRoot, string relative)
    {
        var full = Path.Combine(scanRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[1024]);
    }

    // Eight episodes of "Death Note" season 1, numbered 1..8 (titles carry no digits).
    private static readonly string[] DeathNoteSeason1 =
        { "Rebirth", "Confrontation", "Dealings", "Pursuit", "Tactics", "Unraveling", "Overcast", "Glare" };

    // Eight episodes of season 2 filed with an absolute count that continues at 09..16.
    private static readonly string[] DeathNoteSeason2 =
        { "Encounter", "Silence", "Doubt", "Love", "Friend", "Guidance", "Performance", "Decision" };

    private static void WriteDeathNoteSeason1(string scan)
    {
        for (var i = 0; i < DeathNoteSeason1.Length; i++)
            WriteFile(scan, $@"Death Note\Season 1\{i + 1} {DeathNoteSeason1[i]}.mkv");
    }

    private static void WriteDeathNoteSeason2(string scan)
    {
        for (var i = 0; i < DeathNoteSeason2.Length; i++)
            WriteFile(scan, $@"Death Note\Season 2\{i + 9:00} {DeathNoteSeason2[i]}.mkv");
    }

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

    [Fact]
    public async Task AddConfirmed_AbsoluteNumbering_RenumbersLaterSeasonWithinBatch()
    {
        var scan = NewScanRoot();
        try
        {
            // Death Note filed with a single running count: season 1 is 1..8, season 2 continues at 09..16.
            WriteDeathNoteSeason1(scan);
            WriteDeathNoteSeason2(scan);

            var library = NewLibrary();
            await library.AddConfirmedAsync(await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            var series = Assert.Single(await library.GetLibraryAsync());
            var episodes = await library.GetEpisodesAsync(series.Item.Id);

            // The "09" file is the 9th episode overall but the 1st of season 2.
            var encounter = episodes.Single(e => e.FilePath!.Contains("Encounter"));
            Assert.Equal(2, encounter.SeasonNumber);
            Assert.Equal(1, encounter.EpisodeNumber);

            var decision = episodes.Single(e => e.FilePath!.Contains("Decision"));
            Assert.Equal(2, decision.SeasonNumber);
            Assert.Equal(8, decision.EpisodeNumber);

            // Season 1 is untouched.
            var rebirth = episodes.Single(e => e.FilePath!.Contains("Rebirth"));
            Assert.Equal(1, rebirth.SeasonNumber);
            Assert.Equal(1, rebirth.EpisodeNumber);
        }
        finally { Directory.Delete(scan, recursive: true); }
    }

    [Fact]
    public async Task AddConfirmed_AbsoluteNumbering_RenumbersAcrossSeparateImportPasses()
    {
        var scan = NewScanRoot();
        try
        {
            var library = NewLibrary();

            // Pass 1: season 1 only.
            WriteDeathNoteSeason1(scan);
            await library.AddConfirmedAsync(await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            // Pass 2: season 2, whose files continue the absolute count at 09 — corrected using the
            // season 1 episodes already in the library.
            WriteDeathNoteSeason2(scan);
            await library.AddConfirmedAsync(await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            var series = Assert.Single(await library.GetLibraryAsync());
            var episodes = await library.GetEpisodesAsync(series.Item.Id);

            var encounter = episodes.Single(e => e.FilePath!.Contains("Encounter"));
            Assert.Equal(2, encounter.SeasonNumber);
            Assert.Equal(1, encounter.EpisodeNumber);
            Assert.Equal(16, episodes.Count);
        }
        finally { Directory.Delete(scan, recursive: true); }
    }

    [Fact]
    public async Task AddConfirmed_AbsoluteNumbering_CorrectsEarlierStoredSeasonWhenBaseArrivesLater()
    {
        var scan = NewScanRoot();
        try
        {
            var library = NewLibrary();

            // Season 2 imported first: no base to anchor to, so it is stored with its absolute numbers.
            WriteDeathNoteSeason2(scan);
            await library.AddConfirmedAsync(await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            var seriesId = (await library.GetLibraryAsync()).Single().Item.Id;
            var stored = (await library.GetEpisodesAsync(seriesId)).Single(e => e.FilePath!.Contains("Encounter"));
            Assert.Equal(9, stored.EpisodeNumber); // not yet corrected — nothing to anchor to

            // Season 1 arrives and establishes the boundary; the previously-stored season 2 is corrected.
            WriteDeathNoteSeason1(scan);
            await library.AddConfirmedAsync(await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            var encounter = (await library.GetEpisodesAsync(seriesId)).Single(e => e.FilePath!.Contains("Encounter"));
            Assert.Equal(2, encounter.SeasonNumber);
            Assert.Equal(1, encounter.EpisodeNumber);
            Assert.Equal("S02E01", encounter.Title); // placeholder regenerated to the corrected number
        }
        finally { Directory.Delete(scan, recursive: true); }
    }

    [Fact]
    public async Task AddConfirmed_WithinSeasonNumbering_IsNotRenumbered_AndIsIdempotent()
    {
        var scan = NewScanRoot();
        try
        {
            var library = NewLibrary();
            WriteFile(scan, @"The Office\Season 1\The.Office.S01E01.mkv");
            WriteFile(scan, @"The Office\Season 1\The.Office.S01E02.mkv");
            WriteFile(scan, @"The Office\Season 1\The.Office.S01E03.mkv");
            WriteFile(scan, @"The Office\Season 2\The.Office.S02E01.mkv");
            WriteFile(scan, @"The Office\Season 2\The.Office.S02E02.mkv");
            WriteFile(scan, @"The Office\Season 2\The.Office.S02E03.mkv");

            var candidates = await library.ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0);
            await library.AddConfirmedAsync(candidates);
            await library.AddConfirmedAsync(candidates); // re-import must not shift anything

            var seriesId = (await library.GetLibraryAsync()).Single().Item.Id;
            var episodes = await library.GetEpisodesAsync(seriesId);

            var s2e1 = episodes.Single(e => e.FilePath!.Contains("S02E01"));
            Assert.Equal(2, s2e1.SeasonNumber);
            Assert.Equal(1, s2e1.EpisodeNumber); // season 2 already restarts at 1 — left alone
            Assert.Equal(6, episodes.Count);
        }
        finally { Directory.Delete(scan, recursive: true); }
    }

    [Fact]
    public async Task AddConfirmed_Renumber_RegeneratesPlaceholderTitle_ButPreservesRealTitle()
    {
        var scan = NewScanRoot();
        try
        {
            // Seed Death Note with season 2 stored absolutely (9, 10): one still-placeholder title,
            // one already given a real (e.g. metadata) title.
            var seededDir = Path.Combine(_root, "dn-s2");
            Directory.CreateDirectory(seededDir);
            var ep9Path = Path.Combine(seededDir, "09 Encounter.mkv");
            var ep10Path = Path.Combine(seededDir, "10 Silence.mkv");
            File.WriteAllBytes(ep9Path, new byte[10]);
            File.WriteAllBytes(ep10Path, new byte[10]);

            int seriesId;
            await using (var db = new MosaicDbContext(_options))
            {
                var series = new MediaItem { Kind = MediaKind.Series, Title = "Death Note", FolderPath = seededDir, DateAdded = DateTimeOffset.UtcNow };
                series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "S02E09", FilePath = ep9Path, FolderPath = seededDir, SeasonNumber = 2, EpisodeNumber = 9, DateAdded = DateTimeOffset.UtcNow });
                series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "Silence (Real)", FilePath = ep10Path, FolderPath = seededDir, SeasonNumber = 2, EpisodeNumber = 10, DateAdded = DateTimeOffset.UtcNow });
                db.MediaItems.Add(series);
                await db.SaveChangesAsync();
                seriesId = series.Id;
            }

            // Importing season 1 establishes the boundary that renumbers the stored season 2.
            WriteDeathNoteSeason1(scan);
            await NewLibrary().AddConfirmedAsync(await NewLibrary().ScanFoldersAsync(new[] { scan }, minFileSizeBytes: 0));

            var episodes = await NewLibrary().GetEpisodesAsync(seriesId);
            var ep9 = episodes.Single(e => e.FilePath == ep9Path);
            var ep10 = episodes.Single(e => e.FilePath == ep10Path);

            Assert.Equal(1, ep9.EpisodeNumber);
            Assert.Equal("S02E01", ep9.Title);          // placeholder regenerated

            Assert.Equal(2, ep10.EpisodeNumber);
            Assert.Equal("Silence (Real)", ep10.Title); // real title preserved
        }
        finally { Directory.Delete(scan, recursive: true); }
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
