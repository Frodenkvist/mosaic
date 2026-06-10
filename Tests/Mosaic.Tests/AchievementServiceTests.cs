using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        var newly = (await service.ScanUnlocksAsync(gameId)).NewlyUnlocked;

        Assert.Single(newly);
        Assert.Equal("ACH1", newly[0].ApiName);
        Assert.Equal(new[] { "First" }, unlocked);   // event raised once, with the display name
        Assert.Contains(gameId, changed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), newly[0].UnlockedAt); // file timestamp used

        // The emulator file disappears; a re-scan must NOT re-lock and must not re-raise.
        unlocked.Clear();
        File.Delete(Path.Combine(gameDir, "achievements.json"));
        var second = (await service.ScanUnlocksAsync(gameId)).NewlyUnlocked;

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
        var newly = (await service.ScanUnlocksAsync(gameId)).NewlyUnlocked;

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

    [Fact]
    public async Task Scan_MatchesEmulatorKey_CaseInsensitively()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900010);
        await ResolveSchemaAsync(gameId, ("ACH_Win", "First", 0));
        var service = NewService("key", _ => Bytes());

        // The emulator file spells the key in a different case than the schema.
        WriteGoldberg(gameDir, ("ach_win", 1609459200));
        var result = await service.ScanUnlocksAsync(gameId);

        var newly = Assert.Single(result.NewlyUnlocked);
        Assert.Equal("ACH_Win", newly.ApiName);           // matched the definition despite the casing
        Assert.Equal(1, result.Diagnostic.MatchedCount);
        Assert.Equal(0, result.Diagnostic.UnmatchedCount);
    }

    [Fact]
    public async Task Scan_CountsUnmatchedKeys_InDiagnostic_WithoutLosingMatchedUnlock()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900011);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", _ => Bytes());

        // One key matches the schema; one does not (e.g. wrong App ID or stale schema).
        WriteGoldberg(gameDir, ("ACH1", 1609459200), ("ZZ_UNKNOWN", 1609459200));
        var result = await service.ScanUnlocksAsync(gameId);

        Assert.Equal("ACH1", Assert.Single(result.NewlyUnlocked).ApiName); // matched unlock not lost
        Assert.Equal(1, result.Diagnostic.MatchedCount);
        Assert.Equal(1, result.Diagnostic.UnmatchedCount);
        Assert.Contains("ZZ_UNKNOWN", result.Diagnostic.SampleUnmatchedKeys); // surfaced, not silently dropped
    }

    [Fact]
    public async Task Scan_FindsFile_InLocalSaveRedirectedLocation()
    {
        const int appId = 9900012;
        var (gameId, gameDir) = await AddLinkedGameAsync(appId);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", _ => Bytes());

        // gbe_fork redirects saves via steam_settings\local_save.txt -> "saves".
        var settingsDir = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "local_save.txt"), "saves");
        var saveDir = Path.Combine(gameDir, "saves", appId.ToString());
        Directory.CreateDirectory(saveDir);
        File.WriteAllText(Path.Combine(saveDir, "achievements.json"),
            "{\"ACH1\":{\"earned\":true,\"earned_time\":1609459200}}");

        var result = await service.ScanUnlocksAsync(gameId);

        Assert.Equal("ACH1", Assert.Single(result.NewlyUnlocked).ApiName);
    }

    [Fact]
    public async Task SessionEnded_ReconcileRetry_CapturesAnUnlockWrittenAsTheGameExits()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900013);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var tracker = new FakePlayTracker();
        var service = NewService("key", _ => Bytes(), tracker);

        // Session ends with no file yet; the emulator flushes the unlock a moment later.
        tracker.RaiseSessionEnded(gameId);
        await Task.Delay(250); // let the first reconcile attempt run and find nothing
        WriteGoldberg(gameDir, ("ACH1", 1609459200));

        // The bounded retry should still catch it.
        await WaitUntilAsync(async () => (await service.GetProgressAsync(gameId)).Unlocked == 1);
        Assert.Equal((1, 1), await service.GetProgressAsync(gameId));
    }

    [Fact]
    public async Task Scan_WithNoFile_ProducesDiagnostic_ExplainingWhy()
    {
        var (gameId, _) = await AddLinkedGameAsync(appId: 9900014);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", _ => Bytes());

        // No emulator file exists anywhere.
        var result = await service.ScanUnlocksAsync(gameId);

        Assert.Empty(result.NewlyUnlocked);
        Assert.True(result.Diagnostic.LocationsConsidered > 0);   // it did search known locations
        Assert.Equal(0, result.Diagnostic.LocationsFound);
        Assert.Contains("No recognized achievement file", result.Diagnostic.Summary);
    }

    [Fact]
    public async Task GenerateSchema_WritesGbeForkSchema_ForLinkedGameWithDefinitions()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900020);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0), ("ACH2", "Second", 1));
        var service = NewService("key", _ => Bytes());

        var result = await service.GenerateEmulatorSchemaAsync(gameId);

        var target = Path.Combine(gameDir, "steam_settings", "achievements.json");
        Assert.True(result.Written);
        Assert.Equal(target, result.Path);
        Assert.True(File.Exists(target));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("ACH1", doc.RootElement[0].GetProperty("name").GetString());
        Assert.Equal("1", doc.RootElement[1].GetProperty("hidden").GetString()); // ACH2 was hidden
    }

    [Fact]
    public async Task GenerateSchema_Refuses_WhenNoDefinitionsAndNoApiKey()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900021);
        var service = NewService(apiKey: null, _ => throw new InvalidOperationException("must not call Steam"));

        var result = await service.GenerateEmulatorSchemaAsync(gameId);

        Assert.False(result.Written);
        Assert.False(File.Exists(Path.Combine(gameDir, "steam_settings", "achievements.json")));
        Assert.Contains("Steam Web API key", result.Note);
    }

    [Fact]
    public async Task GenerateSchema_DoesNotOverwriteExisting_UnlessOverwriteRequested()
    {
        var (gameId, gameDir) = await AddLinkedGameAsync(appId: 9900022);
        await ResolveSchemaAsync(gameId, ("ACH1", "First", 0));
        var service = NewService("key", _ => Bytes());

        var target = Path.Combine(gameDir, "steam_settings", "achievements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "SENTINEL"); // a hand-made schema must not be clobbered

        var blocked = await service.GenerateEmulatorSchemaAsync(gameId); // overwrite: false
        Assert.False(blocked.Written);
        Assert.True(blocked.RequiresOverwriteConfirmation);
        Assert.Equal(target, blocked.Path);
        Assert.Equal("SENTINEL", await File.ReadAllTextAsync(target)); // left untouched

        var written = await service.GenerateEmulatorSchemaAsync(gameId, overwrite: true);
        Assert.True(written.Written);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        Assert.Equal("ACH1", doc.RootElement[0].GetProperty("name").GetString());
    }

    // --- helpers ---

    private AchievementService NewService(string? apiKey, Func<HttpRequestMessage, HttpResponseMessage> responder,
        IPlayTracker? tracker = null)
    {
        var client = new SteamWebApiClient(new HttpClient(new StubHttpMessageHandler(responder)));
        return new AchievementService(
            new TestDbContextFactory(_options), _paths, new AchievementSettingsStub(apiKey),
            client, tracker ?? new FakePlayTracker(), NullLogger<AchievementService>.Instance);
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
