using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

public class MediaArtworkServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_mart_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mosaic_martdir_{Guid.NewGuid():N}");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly TestDbContextFactory _factory;
    private readonly AppPaths _paths;

    public MediaArtworkServiceTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _factory = new TestDbContextFactory(_options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    [Fact]
    public async Task Fetch_MatchesMovieByTitleAndYear_CachesPoster_AdoptsYearAndId()
    {
        var movieId = await SeedMovieAsync("Inception", year: null);
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/movie"))
                return Json("{\"results\":[{\"id\":27205,\"title\":\"Inception\",\"release_date\":\"2010-07-16\"," +
                            "\"poster_path\":\"/p.jpg\",\"backdrop_path\":\"/b.jpg\",\"overview\":\"o\"}]}");
            return Bytes(); // any image.tmdb.org request
        });

        await service.FetchArtworkAsync(movieId);

        var movie = await GetItemAsync(movieId, includeArtwork: true);
        Assert.Equal(27205, movie.TmdbId);
        Assert.Equal(2010, movie.Year);
        var poster = movie.Artwork.Single(a => a.Kind == MediaArtworkKind.Poster);
        Assert.True(File.Exists(poster.LocalPath), "the poster should be cached on disk");
        Assert.False(poster.IsManualOverride);
    }

    [Fact]
    public async Task Fetch_FillsEpisodeTitlesAndStills_ForMatchedSeries()
    {
        var (seriesId, ep) = await SeedSeriesAsync("Breaking Bad");
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/tv"))
                return Json("{\"results\":[{\"id\":1396,\"name\":\"Breaking Bad\",\"first_air_date\":\"2008-01-20\"," +
                            "\"poster_path\":\"/p.jpg\",\"backdrop_path\":\"/b.jpg\"}]}");
            if (url.Contains("/season/1"))
                return Json("{\"episodes\":[{\"episode_number\":1,\"name\":\"Pilot\",\"still_path\":\"/s1.jpg\"}," +
                            "{\"episode_number\":2,\"name\":\"Cat's in the Bag\",\"still_path\":\"/s2.jpg\"}]}");
            return Bytes();
        });

        await service.FetchArtworkAsync(seriesId);

        await using var db = _factory.CreateDbContext();
        var episodes = await db.MediaItems.Include(m => m.Artwork)
            .Where(m => m.ParentId == seriesId).OrderBy(m => m.EpisodeNumber).ToListAsync();
        Assert.Equal("Pilot", episodes[0].Title);
        Assert.Equal("Cat's in the Bag", episodes[1].Title);
        Assert.All(episodes, e => Assert.Contains(e.Artwork, a => a.Kind == MediaArtworkKind.EpisodeStill));
    }

    [Fact]
    public async Task Fetch_DoesNotReplaceAManualPosterOverride()
    {
        var movieId = await SeedMovieAsync("Inception", year: 2010);

        // Manual poster override.
        var source = Path.Combine(_root, "manual.png");
        await File.WriteAllBytesAsync(source, new byte[] { 9, 9, 9 });
        var service = NewService("key", req =>
            req.RequestUri!.ToString().Contains("search/movie")
                ? Json("{\"results\":[{\"id\":27205,\"title\":\"Inception\",\"release_date\":\"2010-07-16\",\"poster_path\":\"/auto.jpg\"}]}")
                : Bytes());
        var manualPath = await service.SetManualOverrideAsync(movieId, MediaArtworkKind.Poster, source);

        // Refetch must leave the manual poster intact.
        await service.FetchArtworkAsync(movieId, refetch: true);

        var movie = await GetItemAsync(movieId, includeArtwork: true);
        var poster = movie.Artwork.Single(a => a.Kind == MediaArtworkKind.Poster);
        Assert.True(poster.IsManualOverride);
        Assert.Equal(manualPath, poster.LocalPath);
    }

    [Fact]
    public async Task NoApiKey_DisablesFetch_ButManualOverrideStillWorks()
    {
        var movieId = await SeedMovieAsync("Inception", year: 2010);
        var service = NewService(apiKey: null, _ => throw new InvalidOperationException("must not call TMDB"));

        await service.FetchArtworkAsync(movieId); // no key -> no-op, no HTTP

        await using (var db = _factory.CreateDbContext())
            Assert.Empty(await db.MediaArtwork.ToListAsync());

        // Manual override remains fully functional without a key.
        var source = Path.Combine(_root, "manual.png");
        await File.WriteAllBytesAsync(source, new byte[] { 1 });
        await service.SetManualOverrideAsync(movieId, MediaArtworkKind.Poster, source);

        var movie = await GetItemAsync(movieId, includeArtwork: true);
        Assert.Contains(movie.Artwork, a => a.Kind == MediaArtworkKind.Poster && a.IsManualOverride);
    }

    [Fact]
    public async Task Fetch_MatchesSeries_ByOriginalName_WhenLocalizedNameDiffers()
    {
        // Folder is named with the romanized title; TMDB's localized name is the English release title.
        var (seriesId, _) = await SeedSeriesAsync("Yomi no Tsugai");
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/tv"))
                return Json("{\"results\":[{\"id\":260463,\"name\":\"Daemons of the Shadow Realm\"," +
                            "\"original_name\":\"Yomi no Tsugai\",\"first_air_date\":\"2026-04-04\",\"poster_path\":\"/p.jpg\"}]}");
            if (url.Contains("/season/")) return Json("{\"episodes\":[]}");
            return Bytes();
        });

        await service.FetchArtworkAsync(seriesId);

        var series = await GetItemAsync(seriesId, includeArtwork: true);
        Assert.Equal(260463, series.TmdbId);
        Assert.Contains(series.Artwork, a => a.Kind == MediaArtworkKind.Poster);
    }

    [Fact]
    public async Task Fetch_MatchesSeries_ViaAlternativeTitles_WhenNameAndNativeOriginalDiffer()
    {
        // TMDB's localized name is English and its original_name is the native (Japanese) script —
        // neither token-matches the romanized folder name. The romanization lives in /alternative_titles.
        var (seriesId, _) = await SeedSeriesAsync("Yomi no Tsugai");
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/tv"))
                return Json("{\"results\":[" +
                            "{\"id\":999,\"name\":\"Some Other Show\",\"original_name\":\"Some Other Show\",\"first_air_date\":\"2020-01-01\"}," +
                            "{\"id\":260463,\"name\":\"Daemons of the Shadow Realm\",\"original_name\":\"\\u9ec4\\u6cc9\\u306e\\u30c4\\u30ac\\u30a4\",\"first_air_date\":\"2026-04-04\",\"poster_path\":\"/p.jpg\"}]}");
            if (url.Contains("/alternative_titles"))
                return url.Contains("/tv/260463/")
                    ? Json("{\"results\":[{\"iso_3166_1\":\"JP\",\"title\":\"Yomi no Tsugai\",\"type\":\"Romaji\"}]}")
                    : Json("{\"results\":[]}");
            if (url.Contains("/season/")) return Json("{\"episodes\":[]}");
            return Bytes();
        });

        await service.FetchArtworkAsync(seriesId);

        var series = await GetItemAsync(seriesId, includeArtwork: true);
        Assert.Equal(260463, series.TmdbId);
        Assert.Contains(series.Artwork, a => a.Kind == MediaArtworkKind.Poster);
    }

    [Fact]
    public async Task Fetch_AdoptsSoleSearchResult_AsLastResort_WhenNoTitleMatches()
    {
        // No localized/original/alternative title matches, but TMDB resolved the specific query to a single
        // result — treat it as correct (the conservative gate would otherwise leave foreign media art-less).
        var (seriesId, _) = await SeedSeriesAsync("Yomi no Tsugai");
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/tv"))
                return Json("{\"results\":[{\"id\":260463,\"name\":\"Daemons of the Shadow Realm\"," +
                            "\"original_name\":\"\\u9ec4\\u6cc9\\u306e\\u30c4\\u30ac\\u30a4\",\"first_air_date\":\"2026-04-04\",\"poster_path\":\"/p.jpg\"}]}");
            if (url.Contains("/alternative_titles")) return Json("{\"results\":[]}");
            if (url.Contains("/season/")) return Json("{\"episodes\":[]}");
            return Bytes();
        });

        await service.FetchArtworkAsync(seriesId);

        var series = await GetItemAsync(seriesId, includeArtwork: true);
        Assert.Equal(260463, series.TmdbId);
    }

    [Fact]
    public async Task Fetch_DoesNotMatch_WhenMultipleDissimilarResults_AndNoAlternativeTitles()
    {
        // Precision guard: several unrelated results, none similar and no alternative title matches, and not a
        // single unambiguous result -> no match (red triangle), rather than guessing.
        var movieId = await SeedMovieAsync("Totally Made Up Title", year: null);
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("search/movie"))
                return Json("{\"results\":[" +
                            "{\"id\":1,\"title\":\"Alpha\",\"original_title\":\"Alpha\"}," +
                            "{\"id\":2,\"title\":\"Beta\",\"original_title\":\"Beta\"}]}");
            if (url.Contains("/alternative_titles")) return Json("{\"titles\":[]}");
            return Bytes();
        });

        await service.FetchArtworkAsync(movieId);

        var movie = await GetItemAsync(movieId, includeArtwork: true);
        Assert.Null(movie.TmdbId);
        Assert.Empty(movie.Artwork);
    }

    // --- helpers ---

    private MediaArtworkService NewService(string? apiKey, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new TmdbClient(new HttpClient(new StubHttpMessageHandler(responder)));
        return new MediaArtworkService(_factory, _paths, new MediaSettingsStub(apiKey), client);
    }

    private async Task<int> SeedMovieAsync(string title, int? year)
    {
        await using var db = _factory.CreateDbContext();
        var movie = new MediaItem { Kind = MediaKind.Movie, Title = title, Year = year, FilePath = Path.Combine(_root, $"{title}.mkv"), FolderPath = _root, DateAdded = DateTimeOffset.UtcNow };
        db.MediaItems.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private async Task<(int SeriesId, int[] EpisodeIds)> SeedSeriesAsync(string title)
    {
        await using var db = _factory.CreateDbContext();
        var series = new MediaItem { Kind = MediaKind.Series, Title = title, FolderPath = _root, DateAdded = DateTimeOffset.UtcNow };
        series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "E1", FilePath = Path.Combine(_root, "e1.mkv"), FolderPath = _root, SeasonNumber = 1, EpisodeNumber = 1, DateAdded = DateTimeOffset.UtcNow });
        series.Episodes.Add(new MediaItem { Kind = MediaKind.Episode, Title = "E2", FilePath = Path.Combine(_root, "e2.mkv"), FolderPath = _root, SeasonNumber = 1, EpisodeNumber = 2, DateAdded = DateTimeOffset.UtcNow });
        db.MediaItems.Add(series);
        await db.SaveChangesAsync();
        return (series.Id, series.Episodes.Select(e => e.Id).ToArray());
    }

    private async Task<MediaItem> GetItemAsync(int id, bool includeArtwork = false)
    {
        await using var db = _factory.CreateDbContext();
        var query = db.MediaItems.AsNoTracking();
        if (includeArtwork)
            query = query.Include(m => m.Artwork);
        return await query.FirstAsync(m => m.Id == id);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) };

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
