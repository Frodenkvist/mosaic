using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>Settings stub carrying a Steam Web API key for achievement resolution.</summary>
internal sealed class AchievementSettingsStub : ISettingsService
{
    private readonly AppSettings _settings;
    public AchievementSettingsStub(string? steamWebApiKey) =>
        _settings = new AppSettings { SteamWebApiKey = steamWebApiKey };
    public AppSettings Current => _settings;
    public event EventHandler? Changed { add { } remove { } }
    public Task SaveAsync() => Task.CompletedTask;
}

/// <summary>Play tracker stub that lets a test raise session events on demand.</summary>
internal sealed class FakePlayTracker : IPlayTracker
{
    public event EventHandler<int>? SessionStarted;
    public event EventHandler<int>? SessionEnded;
    public void RaiseSessionStarted(int gameId) => SessionStarted?.Invoke(this, gameId);
    public void RaiseSessionEnded(int gameId) => SessionEnded?.Invoke(this, gameId);
    public DateTimeOffset? GetRunningSince(int gameId) => null;
    public Task<bool> LaunchAsync(int gameId) => Task.FromResult(false);
    public bool IsRunning(int gameId) => false;
    public Task<int> ReconcileOpenSessionsAsync() => Task.FromResult(0);
}

public class AchievementServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mosaic_ach_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mosaic_achdir_{Guid.NewGuid():N}");
    private readonly DbContextOptions<MosaicDbContext> _options;
    private readonly AppPaths _paths;

    public AchievementServiceTests()
    {
        _options = new DbContextOptionsBuilder<MosaicDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var db = new MosaicDbContext(_options);
        db.Database.EnsureCreated();
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    [Fact]
    public async Task Refresh_ResolvesSchema_CachesIcons_AndPreservesUnlocksAcrossRefresh()
    {
        var (gameId, _) = await AddLinkedGameAsync(appId: 9900001);
        var service = NewService("key", req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("GetSchemaForGame"))
                return Json(Schema(("ACH1", "First", 0), ("ACH2", "Second", 1)));
            return Bytes(); // any icon URL
        });

        await service.RefreshAsync(gameId);

        var after = await service.GetAchievementsAsync(gameId);
        Assert.Equal(2, after.Count);
        var ach1 = after.Single(a => a.ApiName == "ACH1");
        Assert.Equal("First", ach1.DisplayName);
        Assert.True(after.Single(a => a.ApiName == "ACH2").Hidden);
        Assert.True(File.Exists(ach1.IconUnlockedPath), "the unlocked icon should be cached on disk");

        // Unlock ACH1 manually, then refresh again with an expanded schema.
        await service.SetUnlockedAsync(gameId, ach1.Id, true);
        var service2 = NewService("key", req =>
            req.RequestUri!.ToString().Contains("GetSchemaForGame")
                ? Json(Schema(("ACH1", "First (renamed)", 0), ("ACH2", "Second", 1), ("ACH3", "Third", 0)))
                : Bytes());

        await service2.RefreshAsync(gameId);

        var refreshed = await service2.GetAchievementsAsync(gameId);
        Assert.Equal(3, refreshed.Count);                                    // new definition added
        var ach1After = refreshed.Single(a => a.ApiName == "ACH1");
        Assert.Equal("First (renamed)", ach1After.DisplayName);              // definition updated
        Assert.True(ach1After.IsUnlocked);                                   // unlock preserved
        Assert.True(ach1After.IsManualUnlock);                               // manual flag preserved
    }

    [Fact]
    public async Task Scan_DetectsNewUnlock_RaisesEvent_AndIsMonotonic()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900002);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0), ("ACH2", "Second", 0));
        var service = NewService("key", _ => Bytes());

        var unlocked = new List<string>();
        var changed = new List<int>();
        service.AchievementUnlocked += (_, e) => unlocked.Add(e.AchievementName);
        service.AchievementsChanged += (_, id) => changed.Add(id);

        // Goldberg-style file written next to the game with ACH1 earned.
        WriteGoldberg(gameDir, ("ACH1", 1609459200));
        var newly = await service.ScanUnlocksAsync(gameId);

        Assert.Single(newly);
        Assert.Equal("ACH1", newly[0].ApiName);
        Assert.Equal(new[] { "First" }, unlocked);   // event raised once, with the display name
        Assert.Contains(gameId, changed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), newly[0].UnlockedAt); // file timestamp used

        // The emulator file disappears; a re-scan must NOT re-lock and must not re-raise.
        unlocked.Clear();
        File.Delete(Path.Combine(gameDir, "achievements.json"));
        var second = await service.ScanUnlocksAsync(gameId);

        Assert.Empty(second);
        Assert.Empty(unlocked);
        var still = await service.GetAchievementsAsync(gameId);
        Assert.True(still.Single(a => a.ApiName == "ACH1").IsUnlocked); // monotonic: stays unlocked
    }

    [Fact]
    public async Task Scan_UsesDetectionTime_WhenFileHasNoTimestamp()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900003);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", _ => Bytes());

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        WriteGoldberg(gameDir, ("ACH1", 0)); // earned but no usable timestamp
        var newly = await service.ScanUnlocksAsync(gameId);

        Assert.Single(newly);
        Assert.NotNull(newly[0].UnlockedAt);
        Assert.True(newly[0].UnlockedAt >= before); // detection time, not epoch
    }

    [Fact]
    public async Task SessionEnded_ReconcileScan_CapturesAnUnlockMissedWhileWatching()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900004);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var tracker = new FakePlayTracker();
        var service = NewService("key", _ => Bytes(), tracker);

        // Simulate: game ran, the watcher missed the write, the file now has the unlock.
        WriteGoldberg(gameDir, ("ACH1", 1609459200));
        tracker.RaiseSessionEnded(gameId); // OnSessionEndedAsync runs the final reconcile scan (fire-and-forget)

        // The reconcile scan runs on a background task; wait for it to land.
        await WaitUntilAsync(async () =>
        {
            var (unlocked, _) = await service.GetProgressAsync(gameId);
            return unlocked == 1;
        });

        var (u, t) = await service.GetProgressAsync(gameId);
        Assert.Equal((1, 1), (u, t));
    }

    [Fact]
    public async Task ManualMode_AddAndToggle_WorksWithoutSchema_AndSurvivesRefresh()
    {
        // A linked game so a later refresh runs; manual unlock must survive it.
        var (gameId, _) = await AddLinkedGameAsync(appId: 9900005);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", req =>
            req.RequestUri!.ToString().Contains("GetSchemaForGame")
                ? Json(Schema(("ACH1", "First", 0))) : Bytes());

        // A user-defined achievement coexisting with the Steam-sourced one.
        var manual = await service.AddManualAchievementAsync(gameId, "Beat it blindfolded");
        Assert.True((await service.GetAchievementsAsync(gameId)).Single(a => a.Id == manual.Id).IsManualDefinition);

        await service.SetUnlockedAsync(gameId, manual.Id, true);
        Assert.Equal((1, 2), await service.GetProgressAsync(gameId));

        await service.RefreshAsync(gameId); // re-resolve Steam schema

        var manualAfter = (await service.GetAchievementsAsync(gameId)).Single(a => a.Id == manual.Id);
        Assert.True(manualAfter.IsUnlocked);          // manual unlock preserved across refresh
        Assert.True(manualAfter.IsManualDefinition);  // still a manual definition (schema didn't claim it)

        // Manual re-lock is allowed (deliberate user action).
        await service.SetUnlockedAsync(gameId, manual.Id, false);
        Assert.Equal((0, 2), await service.GetProgressAsync(gameId));
    }

    [Fact]
    public async Task NoApiKey_DisablesResolution_ButManualStillWorks()
    {
        var (gameId, _) = await AddLinkedGameAsync(appId: 9900006);
        var service = NewService(apiKey: null, _ => throw new InvalidOperationException("must not call Steam"));

        Assert.False(service.IsAutoResolutionAvailable);

        await service.RefreshAsync(gameId); // no key -> no-op, no HTTP call
        Assert.Empty(await service.GetAchievementsAsync(gameId));

        // Manual marking remains fully functional.
        var manual = await service.AddManualAchievementAsync(gameId, "Pacifist run");
        await service.SetUnlockedAsync(gameId, manual.Id, true);
        Assert.Equal((1, 1), await service.GetProgressAsync(gameId));
    }

    [Fact]
    public async Task RemovingGame_CascadesAchievements_AndDeletesCachedIcons()
    {
        var (gameId, _) = await AddLinkedGameAsync(appId: 9900007);
        var iconPath = Path.Combine(_paths.AchievementsDirectory, "9900007_ACH1_u.jpg");
        await File.WriteAllBytesAsync(iconPath, new byte[] { 1, 2, 3 });
        await using (var db = new MosaicDbContext(_options))
        {
            db.Achievements.Add(new Achievement
            {
                GameId = gameId,
                ApiName = "ACH1",
                DisplayName = "First",
                IconUnlockedPath = iconPath,
                UnlockedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var library = new GameLibrary(new TestDbContextFactory(_options), _paths, new NoOpArtworkService());
        await library.RemoveGameAsync(gameId);

        await using (var db = new MosaicDbContext(_options))
            Assert.Empty(await db.Achievements.Where(a => a.GameId == gameId).ToListAsync());
        Assert.False(File.Exists(iconPath), "the cached achievement icon should be deleted on game removal");
    }

    // --- helpers ---

    private AchievementService NewService(string? apiKey, Func<HttpRequestMessage, HttpResponseMessage> responder,
        IPlayTracker? tracker = null)
    {
        var client = new SteamWebApiClient(new HttpClient(new StubHttpMessageHandler(responder)));
        return new AchievementService(
            new TestDbContextFactory(_options), _paths, new AchievementSettingsStub(apiKey),
            client, tracker ?? new FakePlayTracker());
    }

    private async Task ResolveSchemaAsync(int gameId, params (string Name, string Display, int Hidden)[] defs)
    {
        var service = NewService("key", req =>
            req.RequestUri!.ToString().Contains("GetSchemaForGame") ? Json(Schema(defs)) : Bytes());
        await service.RefreshAsync(gameId);
    }

    private async Task<(int GameId, string GameDir)> AddLinkedGameAsync(int appId)
    {
        var gameDir = Path.Combine(_root, $"game_{appId}");
        Directory.CreateDirectory(gameDir);
        await using var db = new MosaicDbContext(_options);
        var game = new Game
        {
            Name = $"Game {appId}",
            ExecutablePath = Path.Combine(gameDir, "game.exe"),
            SteamAppId = appId,
            AchievementTrackingEnabled = true,
            AchievementSource = AchievementSource.Auto,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return (game.Id, gameDir);
    }

    private static void WriteGoldberg(string gameDir, params (string Name, long EarnedTime)[] earned)
    {
        var entries = earned.Select(e =>
            $"\"{e.Name}\":{{\"earned\":true,\"earned_time\":{e.EarnedTime}}}");
        File.WriteAllText(Path.Combine(gameDir, "achievements.json"), "{" + string.Join(",", entries) + "}");
    }

    private static string Schema(params (string Name, string Display, int Hidden)[] defs)
    {
        var items = defs.Select(d =>
            $"{{\"name\":\"{d.Name}\",\"displayName\":\"{d.Display}\",\"description\":\"d\",\"hidden\":{d.Hidden}," +
            $"\"icon\":\"https://img/{d.Name}.jpg\",\"icongray\":\"https://img/{d.Name}_g.jpg\"}}");
        return "{\"game\":{\"availableGameStats\":{\"achievements\":[" + string.Join(",", items) + "]}}}";
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Condition was not met within the timeout.");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
