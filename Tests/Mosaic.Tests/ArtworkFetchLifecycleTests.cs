using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>
/// Covers the artwork fetch lifecycle signals (started / updated / failed) that the library
/// tiles use to show a fetching/failed indicator. Drives the real <see cref="ArtworkService"/>
/// and <see cref="SteamGridDbClient"/> over a stubbed HTTP handler so no network is hit.
/// </summary>
public class ArtworkFetchLifecycleTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_art_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly AppPaths _paths = new();

    public ArtworkFetchLifecycleTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new MosaicDbContext(_options);
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task SuccessfulFetch_RaisesStartedThenUpdated_NeverFailed()
    {
        // A scan-style game whose name is auto-derived from its folder, so the matched
        // SteamGridDB title is adopted — that name change alone is a successful update,
        // independent of any image download.
        var gameId = await AddGameAsync("Test Game", @"E:\Games\Test Game\Test Game.exe");
        var service = NewService(
            apiKey: "key",
            // Autocomplete returns a close, differently-named match -> name adoption.
            gamesJson: """{"success":true,"data":[{"id":123,"name":"Test Game Deluxe"}]}""",
            // No assets for any kind: the match resolves but nothing is downloaded.
            assetsJson: """{"success":true,"data":[]}""");

        var (started, updated, failed) = Capture(service);
        await service.FetchArtworkAsync(gameId);

        Assert.Equal(new[] { gameId }, started);
        Assert.Equal(new[] { gameId }, updated);
        Assert.Empty(failed);
        Assert.Equal("Test Game Deluxe", await GameNameAsync(gameId)); // name was adopted
    }

    [Fact]
    public async Task NoMatch_RaisesStartedThenFailed_AndLeavesPlaceholder()
    {
        var gameId = await AddGameAsync("Obscure Game", @"E:\Games\Obscure Game\Obscure Game.exe");
        var service = NewService(
            apiKey: "key",
            gamesJson: """{"success":true,"data":[]}""", // no candidates at all
            assetsJson: """{"success":true,"data":[]}""");

        var (started, updated, failed) = Capture(service);
        await service.FetchArtworkAsync(gameId);

        Assert.Equal(new[] { gameId }, started);
        Assert.Equal(new[] { gameId }, failed);
        Assert.Empty(updated);
        Assert.Equal("Obscure Game", await GameNameAsync(gameId)); // unchanged: placeholder remains
        Assert.Empty(await GameArtworkAsync(gameId));               // no artwork rows written
    }

    [Fact]
    public async Task Timeout_DuringResolve_RaisesFailed_NotStuck()
    {
        // Regression: an HTTP timeout surfaces as TaskCanceledException (an OperationCanceledException)
        // whose token is NOT ours. It must be treated as a failure, not a silent abort that leaves
        // the tile stuck on "fetching" forever.
        var gameId = await AddGameAsync("Test Game", @"E:\Games\Test Game\Test Game.exe");
        var service = NewService("key", _ => throw new OperationCanceledException("simulated timeout"));

        var (started, updated, failed) = Capture(service);
        await service.FetchArtworkAsync(gameId); // a timeout is reported via the event, not thrown to the caller

        Assert.Equal(new[] { gameId }, started);
        Assert.Equal(new[] { gameId }, failed);
        Assert.Empty(updated);
    }

    [Fact]
    public async Task Timeout_DownloadingOneKind_StillSavesTheCover_RaisesUpdated()
    {
        // Regression: a slow hero/logo must not discard an already-downloaded cover nor abort the
        // whole fetch — the grid is saved and the fetch succeeds.
        var root = Path.Combine(Path.GetTempPath(), $"mosaic_artdir_{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        try
        {
            var gameId = await AddGameAsync("Test Game", @"E:\Games\Test Game\Test Game.exe");
            var service = NewService("key", req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("/search/autocomplete/"))
                    return Json("""{"success":true,"data":[{"id":123,"name":"Test Game"}]}""");
                if (url.Contains("/grids/game/"))
                    return Json("""{"success":true,"data":[{"id":1,"url":"https://img.example/grid.png"}]}""");
                if (url.Contains("/heroes/game/") || url.Contains("/logos/game/"))
                    throw new OperationCanceledException("simulated timeout on a slow kind");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) };
            }, paths);

            var (started, updated, failed) = Capture(service);
            await service.FetchArtworkAsync(gameId);

            Assert.Equal(new[] { gameId }, started);
            Assert.Equal(new[] { gameId }, updated);
            Assert.Empty(failed);
            Assert.Contains(await GameArtworkAsync(gameId), a => a.Kind == ArtworkKind.Grid);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NoApiKey_RaisesNoSignals()
    {
        var gameId = await AddGameAsync("Test Game", @"E:\Games\Test Game\Test Game.exe");
        var service = NewService(apiKey: "", gamesJson: "{}", assetsJson: "{}");

        var (started, updated, failed) = Capture(service);
        await service.FetchArtworkAsync(gameId);

        Assert.Empty(started);
        Assert.Empty(updated);
        Assert.Empty(failed);
    }

    [Fact]
    public async Task NothingToFetch_RaisesNoSignals()
    {
        // Artwork already complete (manual overrides for every kind): no fetch is attempted.
        var gameId = await AddGameAsync("Test Game", @"E:\Games\Test Game\Test Game.exe", g =>
        {
            foreach (var kind in new[] { ArtworkKind.Grid, ArtworkKind.Hero, ArtworkKind.Logo })
                g.Artwork.Add(new Artwork { Kind = kind, LocalPath = $"manual_{kind}.png", IsManualOverride = true });
        });
        var service = NewService(apiKey: "key", gamesJson: "{}", assetsJson: "{}");

        var (started, updated, failed) = Capture(service);
        await service.FetchArtworkAsync(gameId);

        Assert.Empty(started);
        Assert.Empty(updated);
        Assert.Empty(failed);
    }

    private static (List<int> Started, List<int> Updated, List<int> Failed) Capture(IArtworkService service)
    {
        var started = new List<int>();
        var updated = new List<int>();
        var failed = new List<int>();
        service.ArtworkFetchStarted += (_, id) => started.Add(id);
        service.ArtworkUpdated += (_, id) => updated.Add(id);
        service.ArtworkFetchFailed += (_, id) => failed.Add(id);
        return (started, updated, failed);
    }

    private ArtworkService NewService(string apiKey, string gamesJson, string assetsJson, AppPaths? paths = null) =>
        NewService(apiKey, req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/search/autocomplete/"))
                return Json(gamesJson);
            if (url.Contains("/grids/game/") || url.Contains("/heroes/game/") || url.Contains("/logos/game/"))
                return Json(assetsJson);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
        }, paths);

    private ArtworkService NewService(string apiKey, Func<HttpRequestMessage, HttpResponseMessage> responder, AppPaths? paths = null)
    {
        var client = new SteamGridDbClient(new HttpClient(new StubHttpMessageHandler(responder)));
        return new ArtworkService(new TestDbContextFactory(_options), paths ?? _paths, new StubSettingsService(apiKey), client);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private async Task<int> AddGameAsync(string name, string exePath, Action<Game>? configure = null)
    {
        await using var db = new MosaicDbContext(_options);
        var game = new Game { Name = name, ExecutablePath = exePath };
        configure?.Invoke(game);
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private async Task<string> GameNameAsync(int gameId)
    {
        await using var db = new MosaicDbContext(_options);
        return (await db.Games.FirstAsync(g => g.Id == gameId)).Name;
    }

    private async Task<List<Artwork>> GameArtworkAsync(int gameId)
    {
        await using var db = new MosaicDbContext(_options);
        return await db.Artwork.Where(a => a.GameId == gameId).ToListAsync();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}

internal sealed class StubSettingsService : ISettingsService
{
    private readonly AppSettings _settings;
    public StubSettingsService(string? apiKey) => _settings = new AppSettings { SteamGridDbApiKey = apiKey };
    public AppSettings Current => _settings;
    public event EventHandler? Changed { add { } remove { } }
    public Task SaveAsync() => Task.CompletedTask;
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return Task.FromResult(_responder(request)); }
        catch (Exception ex) { return Task.FromException<HttpResponseMessage>(ex); }
    }
}
