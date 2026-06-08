using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>
/// Orchestrates achievements: resolves definitions from the Steam Web API (cached), detects unlocks
/// from local Steam-emulator files via pluggable <see cref="IAchievementUnlockSource"/>s, watches
/// those files live during a play session, supports manual marking, and persists everything.
/// Play tracking is untouched — this service only subscribes to its session events. Lifecycle
/// events are raised on background threads; view models marshal them onto the UI via App.RunOnUiAsync.
/// </summary>
public class AchievementService : IAchievementService
{
    // Serializes Steam Web API access so a batch resolve isn't rate-limited (mirrors ArtworkService).
    private static readonly SemaphoreSlim FetchGate = new(1, 1);
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromMilliseconds(400);

    // Bounded reconcile retry on session end: a few attempts so an unlock flushed as the game exits
    // (just after the first scan reads the files) is still caught, without blocking shutdown.
    private const int ReconcileAttempts = 5;
    private static readonly TimeSpan ReconcileRetryDelay = TimeSpan.FromMilliseconds(400);

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly SteamWebApiClient _client;
    private readonly ILogger<AchievementService> _logger;
    private readonly IReadOnlyList<IAchievementUnlockSource> _sources;

    private readonly ConcurrentDictionary<int, GameWatcher> _watchers = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _scanGates = new();

    public AchievementService(
        IDbContextFactory<MosaicDbContext> contextFactory,
        AppPaths paths,
        ISettingsService settings,
        SteamWebApiClient client,
        IPlayTracker tracker,
        ILogger<AchievementService> logger)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _settings = settings;
        _client = client;
        _logger = logger;
        _sources = new IAchievementUnlockSource[] { new SteamEmulatorUnlockSource() };

        // Watch files only while a game is actually running; reconcile when it stops.
        tracker.SessionStarted += (_, id) => _ = Task.Run(() => StartWatching(id));
        tracker.SessionEnded += (_, id) => _ = Task.Run(() => OnSessionEndedAsync(id));
    }

    public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
    public event EventHandler<int>? AchievementsChanged;

    public bool IsAutoResolutionAvailable => !string.IsNullOrWhiteSpace(_settings.Current.SteamWebApiKey);

    public async Task<IReadOnlyList<Achievement>> GetAchievementsAsync(int gameId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Achievements.AsNoTracking()
            .Where(a => a.GameId == gameId)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.DisplayName)
            .ToListAsync();
    }

    public async Task<(int Unlocked, int Total)> GetProgressAsync(int gameId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var total = await db.Achievements.AsNoTracking().CountAsync(a => a.GameId == gameId);
        var unlocked = await db.Achievements.AsNoTracking().CountAsync(a => a.GameId == gameId && a.UnlockedAt != null);
        return (unlocked, total);
    }

    public async Task<IReadOnlyList<SteamApp>> SuggestAppsAsync(int gameId, CancellationToken cancellationToken = default)
    {
        Game? game;
        await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
            game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game is null)
            return Array.Empty<SteamApp>();

        // Reuse the artwork name-resolution heuristics: try the game's name then folder-derived
        // terms, rank every candidate by name similarity, dedupe by appid, best first.
        var ranked = new List<(SteamApp App, double Score)>();
        var seen = new HashSet<int>();
        foreach (var term in ArtworkService.BuildSearchTerms(game))
        {
            foreach (var app in await _client.SearchAppsAsync(term, cancellationToken))
            {
                if (!seen.Add(app.AppId))
                    continue;
                ranked.Add((app, ArtworkService.Similarity(term, app.Name)));
            }
        }
        return ranked.OrderByDescending(c => c.Score).Select(c => c.App).Take(10).ToList();
    }

    public async Task LinkAppIdAsync(int gameId, int appId, CancellationToken cancellationToken = default)
    {
        await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
            if (game is null)
                return;
            game.SteamAppId = appId;
            if (game.AchievementSource == AchievementSource.Disabled)
                game.AchievementSource = AchievementSource.Auto;
            await db.SaveChangesAsync(cancellationToken);
        }
        await RefreshAsync(gameId, cancellationToken);
    }

    public async Task SetUnlinkedAsync(int gameId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null)
            return;
        game.SteamAppId = null;
        await db.SaveChangesAsync();
        AchievementsChanged?.Invoke(this, gameId);
    }

    public async Task SetSourceAsync(int gameId, bool enabled, AchievementSource source)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null)
            return;
        game.AchievementTrackingEnabled = enabled;
        game.AchievementSource = source;
        await db.SaveChangesAsync();
        AchievementsChanged?.Invoke(this, gameId);
    }

    public Task RefreshAsync(int gameId, CancellationToken cancellationToken = default)
    {
        // Cheap guard so a key-less install schedules no background work.
        if (!IsAutoResolutionAvailable)
            return Task.CompletedTask;
        return Task.Run(() => RefreshCoreAsync(gameId, cancellationToken), cancellationToken);
    }

    private async Task RefreshCoreAsync(int gameId, CancellationToken cancellationToken)
    {
        var apiKey = _settings.Current.SteamWebApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await db.Games.Include(g => g.Achievements).FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game?.SteamAppId is not int appId || appId <= 0)
            return;

        IReadOnlyList<SteamAchievementDef>? defs;
        await FetchGate.WaitAsync(cancellationToken);
        try
        {
            defs = await _client.GetSchemaForGameAsync(appId, apiKey, cancellationToken);
        }
        finally
        {
            FetchGate.Release();
        }

        if (defs is null)
            return; // resolution failed (rejected key / network) — leave existing definitions untouched

        var order = 0;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in defs)
        {
            keep.Add(def.ApiName);
            var existing = game.Achievements.FirstOrDefault(a => a.ApiName == def.ApiName);
            // Cache icons best-effort; reuse cached files and tolerate failures.
            var iconUnlocked = await CacheIconAsync(gameId, def.ApiName, def.IconUrl, unlocked: true, cancellationToken);
            var iconLocked = await CacheIconAsync(gameId, def.ApiName, def.IconGrayUrl, unlocked: false, cancellationToken);

            if (existing is null)
            {
                game.Achievements.Add(new Achievement
                {
                    GameId = gameId,
                    ApiName = def.ApiName,
                    DisplayName = def.DisplayName,
                    Description = def.Description,
                    Hidden = def.Hidden,
                    DisplayOrder = order,
                    IconUnlockedPath = iconUnlocked,
                    IconLockedPath = iconLocked,
                    IsManualDefinition = false,
                });
            }
            else
            {
                // Update the definition; never touch unlock state (UnlockedAt / IsManualUnlock).
                existing.DisplayName = def.DisplayName;
                existing.Description = def.Description;
                existing.Hidden = def.Hidden;
                existing.DisplayOrder = order;
                existing.IsManualDefinition = false;
                if (iconUnlocked is not null) existing.IconUnlockedPath = iconUnlocked;
                if (iconLocked is not null) existing.IconLockedPath = iconLocked;
            }
            order++;
        }

        // Drop Steam-sourced definitions no longer in the schema that carry no unlock; keep manual
        // definitions and anything the player has already unlocked.
        var stale = game.Achievements
            .Where(a => !a.IsManualDefinition && a.UnlockedAt is null && !keep.Contains(a.ApiName))
            .ToList();
        foreach (var s in stale)
        {
            DeleteIcon(s.IconUnlockedPath);
            DeleteIcon(s.IconLockedPath);
            game.Achievements.Remove(s);
        }

        await db.SaveChangesAsync(cancellationToken);
        AchievementsChanged?.Invoke(this, gameId);
    }

    public async Task<ScanResult> ScanUnlocksAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var gate = _scanGates.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ScanUnlocksCoreAsync(gameId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ScanResult> ScanUnlocksCoreAsync(int gameId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await db.Games.Include(g => g.Achievements).FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game is null)
            return new ScanResult(Array.Empty<Achievement>(), new ScanDiagnostic { Note = "Game not found." });
        if (!game.AchievementTrackingEnabled)
            return new ScanResult(Array.Empty<Achievement>(),
                new ScanDiagnostic { Note = "Achievement tracking is disabled for this game." });
        if (game.AchievementSource != AchievementSource.Auto)
            return new ScanResult(Array.Empty<Achievement>(),
                new ScanDiagnostic { Note = "Achievement source is set to manual for this game." });

        var source = _sources.FirstOrDefault(s => s.CanHandle(game));
        if (source is null)
            return new ScanResult(Array.Empty<Achievement>(), new ScanDiagnostic
            {
                Note = "This game is not linked to a Steam App ID, so there are no emulator files to scan.",
            });

        SourceReadResult read;
        try
        {
            read = source.ReadUnlocks(game);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading emulator achievement files failed for game {GameId} ({Game})", gameId, game.Name);
            return new ScanResult(Array.Empty<Achievement>(),
                new ScanDiagnostic { Note = "Failed to read the emulator achievement files: " + ex.Message });
        }

        // Match parsed keys to stored definitions case-insensitively, with a separator/case-insensitive
        // normalized fallback (emulator files often differ from the schema only in casing or underscores).
        var byKey = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in game.Achievements)
            byKey.TryAdd(a.ApiName, a);
        var byNormalized = new Dictionary<string, Achievement?>(StringComparer.Ordinal);
        foreach (var a in game.Achievements)
        {
            var norm = Normalize(a.ApiName);
            if (norm.Length == 0)
                continue;
            byNormalized[norm] = byNormalized.ContainsKey(norm) ? null : a; // null marks a collision (skip)
        }

        Achievement? Match(string key)
        {
            if (byKey.TryGetValue(key, out var exact))
                return exact;
            var norm = Normalize(key);
            if (norm.Length > 0 && byNormalized.TryGetValue(norm, out var fuzzy) && fuzzy is not null)
                return fuzzy;
            return null;
        }

        var newlyUnlocked = new List<Achievement>();
        var unmatched = new List<string>();
        var matchedCount = 0;
        foreach (var unlock in read.Unlocks)
        {
            var ach = Match(unlock.ApiName);
            if (ach is null)
            {
                unmatched.Add(unlock.ApiName); // recorded in the diagnostic, never silently dropped
                continue;
            }
            matchedCount++;
            if (ach.UnlockedAt is not null)
                continue; // monotonic: already-unlocked is never re-locked or re-raised
            ach.UnlockedAt = unlock.UnlockedAt ?? DateTimeOffset.UtcNow; // file time when known, else detection time
            ach.IsManualUnlock = false;
            newlyUnlocked.Add(ach);
        }

        var diagnostic = new ScanDiagnostic
        {
            Candidates = read.Candidates,
            ParsedKeyCount = read.Unlocks.Count,
            MatchedCount = matchedCount,
            UnmatchedCount = unmatched.Count,
            SampleUnmatchedKeys = unmatched.Take(10).ToList(),
        };

        if (newlyUnlocked.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Achievement scan for game {GameId} ({Game}): {Summary}",
            gameId, game.Name, diagnostic.Summary);
        if (diagnostic.UnmatchedCount > 0)
            _logger.LogWarning("Game {GameId}: {Count} detected achievement key(s) matched no definition (sample: {Sample})",
                gameId, diagnostic.UnmatchedCount, string.Join(", ", diagnostic.SampleUnmatchedKeys));

        foreach (var ach in newlyUnlocked)
        {
            AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs
            {
                GameId = gameId,
                GameName = game.Name,
                AchievementName = ach.DisplayName,
                IconPath = ach.IconUnlockedPath,
            });
        }
        if (newlyUnlocked.Count > 0)
            AchievementsChanged?.Invoke(this, gameId);

        return new ScanResult(newlyUnlocked, diagnostic);
    }

    /// <summary>Lowercased, alphanumeric-only form of an achievement key, for tolerant key matching.</summary>
    private static string Normalize(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public async Task SetUnlockedAsync(int gameId, int achievementId, bool unlocked)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var ach = await db.Achievements.FirstOrDefaultAsync(a => a.Id == achievementId && a.GameId == gameId);
        if (ach is null)
            return;
        // Manual marking is a deliberate user action, so it may both unlock and re-lock.
        ach.UnlockedAt = unlocked ? DateTimeOffset.UtcNow : null;
        ach.IsManualUnlock = unlocked;
        await db.SaveChangesAsync();
        AchievementsChanged?.Invoke(this, gameId);
    }

    public async Task<Achievement> AddManualAchievementAsync(int gameId, string displayName, string? description = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var maxOrder = await db.Achievements.Where(a => a.GameId == gameId)
            .Select(a => (int?)a.DisplayOrder).MaxAsync() ?? -1;

        var ach = new Achievement
        {
            GameId = gameId,
            ApiName = $"manual_{Guid.NewGuid():N}",
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Untitled achievement" : displayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DisplayOrder = maxOrder + 1,
            IsManualDefinition = true,
        };
        db.Achievements.Add(ach);
        await db.SaveChangesAsync();
        AchievementsChanged?.Invoke(this, gameId);
        return ach;
    }

    // --- Live file watching (scoped to a play session) ---

    private async Task StartWatching(int gameId)
    {
        try
        {
            Game? game;
            await using (var db = await _contextFactory.CreateDbContextAsync())
                game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);

            // Don't watch unlinked / tracking-disabled / manual-only games.
            if (game is null || !game.AchievementTrackingEnabled || game.AchievementSource != AchievementSource.Auto)
                return;
            var source = _sources.FirstOrDefault(s => s.CanHandle(game));
            if (source is null)
                return;

            var watcher = new GameWatcher(gameId, () => ScheduleScan(gameId));
            foreach (var file in source.LocateFiles(game))
                watcher.TryWatch(file);

            // Replace any prior watcher for this game.
            if (_watchers.TryRemove(gameId, out var old))
                old.Dispose();
            _watchers[gameId] = watcher;
        }
        catch (Exception ex)
        {
            // Watching is best-effort; the session-end reconcile scan still captures unlocks.
            _logger.LogWarning(ex, "Could not start achievement file watching for game {GameId}", gameId);
        }
    }

    private void ScheduleScan(int gameId)
    {
        if (_watchers.TryGetValue(gameId, out var watcher))
            watcher.Debounce(WatchDebounce, () => _ = SafeScanAsync(gameId));
    }

    /// <summary>Runs a scan and logs (rather than swallows) any failure; used by the live watcher.</summary>
    private async Task SafeScanAsync(int gameId)
    {
        try
        {
            await ScanUnlocksAsync(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live achievement scan failed for game {GameId}", gameId);
        }
    }

    private async Task OnSessionEndedAsync(int gameId)
    {
        if (_watchers.TryRemove(gameId, out var watcher))
            watcher.Dispose();

        // Final reconcile: a write can land in the gap (and watchers aren't 100% reliable), and some
        // emulators flush their achievement file as the game exits — just after the first scan reads
        // it. Retry a few times over a couple of seconds, stopping as soon as a scan finds an unlock.
        for (var attempt = 0; attempt < ReconcileAttempts; attempt++)
        {
            try
            {
                var result = await ScanUnlocksAsync(gameId);
                if (result.NewlyUnlocked.Count > 0)
                    return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconcile achievement scan failed for game {GameId}", gameId);
            }

            if (attempt < ReconcileAttempts - 1)
            {
                try { await Task.Delay(ReconcileRetryDelay); }
                catch (TaskCanceledException) { return; }
            }
        }
    }

    // --- Icon caching ---

    private async Task<string?> CacheIconAsync(int gameId, string apiName, string? url, bool unlocked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var path = BuildIconPath(gameId, apiName, url, unlocked);
        if (File.Exists(path))
            return path; // reuse cached file

        try
        {
            var bytes = await _client.DownloadAsync(url, ct);
            if (bytes is null || bytes.Length == 0)
                return null;
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null; // best-effort: a missing icon is acceptable
        }
    }

    private string BuildIconPath(int gameId, string apiName, string url, bool unlocked)
    {
        var ext = Path.GetExtension(new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).AbsolutePath : url);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
            ext = ".jpg";
        var safe = Sanitize(apiName);
        return Path.Combine(_paths.AchievementsDirectory, $"{gameId}_{safe}_{(unlocked ? "u" : "l")}{ext}");
    }

    private void DeleteIcon(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && _paths.IsInsideDataDirectory(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Non-fatal.
        }
    }

    private static string Sanitize(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var cleaned = new string(chars);
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    /// <summary>Holds the file watchers and debounce timer for one running game.</summary>
    private sealed class GameWatcher : IDisposable
    {
        private readonly int _gameId;
        private readonly Action _onChange;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _debounce;
        private Action? _pending;
        private bool _disposed;

        public GameWatcher(int gameId, Action onChange)
        {
            _gameId = gameId;
            _onChange = onChange;
            _debounce = new Timer(_ => _pending?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void TryWatch(string file)
        {
            try
            {
                var dir = Path.GetDirectoryName(file);
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(dir))
                    return;
                if (!_watched.Add(dir + "|" + name))
                    return;

                var w = new FileSystemWatcher(dir, name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                FileSystemEventHandler handler = (_, _) => _onChange();
                w.Changed += handler;
                w.Created += handler;
                w.Renamed += (_, _) => _onChange();
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch
            {
                // A watch we can't establish just means we rely on the end-of-session scan.
            }
        }

        public void Debounce(TimeSpan delay, Action action)
        {
            if (_disposed)
                return;
            _pending = action;
            _debounce.Change(delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _disposed = true;
            _debounce.Dispose();
            foreach (var w in _watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { /* ignore */ }
            }
            _watchers.Clear();
        }
    }
}
