using System.IO;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class GameLibrary : IGameLibrary
{
    // Executable name fragments that are almost never the game itself.
    private static readonly string[] JunkNameFragments =
    {
        "unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "directx",
        "dotnet", "dotnetfx", "oalinst", "openal", "crashhandler", "crashreport", "crashpad",
        "crashsender", "crashreportclient", "bugsplat", "sndrpt", "werfault", "uploader",
        "updater", "prereq", "dependenc", "shadercompile", "compileworker", "curl", "ffmpeg",
        "easyanticheat", "battleye", "be_service", "touchup", "webhelper", "notification",
        "diagnostic", "benchmark", "language changer", "languagechanger", "handler", "helper",
        "launcher", "subprocess",
    };

    // Path segments (folders) whose executables are redistributables or tools, not games.
    private static readonly string[] JunkPathSegments =
    {
        "_commonredist", "commonredist", "redist", "_redist", "directx", "vcredist",
        "dotnet", "prerequisites", "prereq", "_cef", "dxredist",
    };

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly AppPaths _paths;
    private readonly IArtworkService _artwork;

    public GameLibrary(
        IDbContextFactory<MosaicDbContext> contextFactory,
        AppPaths paths,
        IArtworkService artwork)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _artwork = artwork;
    }

    public async Task<IReadOnlyList<GameListItem>> GetLibraryAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var games = await db.Games.AsNoTracking()
            .Include(g => g.Artwork)
            .OrderBy(g => g.Name)
            .ToListAsync();

        var statsByGame = await ComputeStatsAsync(db);
        var achievementsByGame = await ComputeAchievementCountsAsync(db);
        return games.Select(g => ToListItem(g, statsByGame, achievementsByGame)).ToList();
    }

    public async Task<IReadOnlyList<GameListItem>> GetRecentlyPlayedAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var games = await db.Games.AsNoTracking()
            .Include(g => g.Artwork)
            .ToListAsync();

        var statsByGame = await ComputeStatsAsync(db);
        var achievementsByGame = await ComputeAchievementCountsAsync(db);
        return games
            .Select(g => ToListItem(g, statsByGame, achievementsByGame))
            .Where(i => i.Stats.HasBeenPlayed)
            .OrderByDescending(i => i.Stats.LastPlayed)
            .ToList();
    }

    public async Task<Game?> GetGameAsync(int id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Games.AsNoTracking()
            .Include(g => g.Artwork)
            .Include(g => g.Sessions)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<GameStats> GetStatsAsync(int gameId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var completed = await db.PlaySessions.AsNoTracking()
            .Where(s => s.GameId == gameId && s.EndedAt != null)
            .Select(s => new { s.DurationSeconds, s.EndedAt })
            .ToListAsync();

        if (completed.Count == 0)
            return GameStats.Empty;

        var total = TimeSpan.FromSeconds(completed.Sum(s => s.DurationSeconds ?? 0));
        var last = completed.Max(s => s.EndedAt);
        return new GameStats(total, last, completed.Count);
    }

    public async Task<Game> AddGameAsync(AddGameRequest request)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var normalized = Path.GetFullPath(request.ExecutablePath);

        if (await db.Games.AnyAsync(g => g.ExecutablePath == normalized))
            throw new DuplicateExecutableException(normalized);

        var game = new Game
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? Path.GetFileNameWithoutExtension(normalized)
                : request.Name.Trim(),
            ExecutablePath = normalized,
            LaunchArguments = NullIfBlank(request.LaunchArguments),
            WorkingDirectory = NullIfBlank(request.WorkingDirectory),
            RealExecutableName = NullIfBlank(request.RealExecutableName),
            SteamAppId = request.SteamAppId is > 0 ? request.SteamAppId : null,
            DateAdded = DateTimeOffset.UtcNow,
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();

        // Best-effort artwork fetch; the game is already usable without it.
        _ = FetchArtworkSafelyAsync(game.Id);
        return game;
    }

    public async Task UpdateGameAsync(Game game)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var existing = await db.Games.FirstOrDefaultAsync(g => g.Id == game.Id)
            ?? throw new InvalidOperationException($"Game {game.Id} not found.");

        var normalized = Path.GetFullPath(game.ExecutablePath);
        if (!string.Equals(normalized, existing.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && await db.Games.AnyAsync(g => g.Id != game.Id && g.ExecutablePath == normalized))
        {
            throw new DuplicateExecutableException(normalized);
        }

        existing.Name = game.Name.Trim();
        existing.ExecutablePath = normalized;
        existing.LaunchArguments = NullIfBlank(game.LaunchArguments);
        existing.WorkingDirectory = NullIfBlank(game.WorkingDirectory);
        existing.RealExecutableName = NullIfBlank(game.RealExecutableName);
        await db.SaveChangesAsync();
    }

    public async Task RemoveGameAsync(int gameId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var game = await db.Games
            .Include(g => g.Artwork)
            .Include(g => g.Achievements)
            .FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null)
            return;

        // Delete cached artwork and achievement-icon files (only those inside our own data directory).
        foreach (var art in game.Artwork)
            TryDeleteCachedFile(art.LocalPath);
        foreach (var ach in game.Achievements)
        {
            TryDeleteCachedFile(ach.IconUnlockedPath);
            TryDeleteCachedFile(ach.IconLockedPath);
        }

        db.Games.Remove(game); // sessions, artwork & achievement rows cascade-delete
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ScanCandidate>> ScanFoldersAsync(IEnumerable<string> folders)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var known = (await db.Games.AsNoTracking().Select(g => g.ExecutablePath).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Group every executable under the game folder it belongs to (the immediate
        // subfolder of the scanned root, or the root itself), then pick the single best
        // candidate per game folder. This collapses a folder of 16 exes into one game.
        var groups = new Dictionary<string, ScanGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            var root = Path.GetFullPath(folder);
            IEnumerable<string> exes;
            try
            {
                exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var exe in exes)
            {
                var full = Path.GetFullPath(exe);
                var (gameDir, displayName) = ResolveGameDir(root, full);

                if (!groups.TryGetValue(gameDir, out var group))
                    groups[gameDir] = group = new ScanGroup(displayName);

                if (known.Contains(full))
                {
                    group.AlreadyInLibrary = true; // a game from this folder is already added
                    continue;
                }

                group.Executables.Add(new ScanExe(full, FileSize(full), IsJunkExecutable(root, full)));
            }
        }

        var candidates = new List<ScanCandidate>();
        foreach (var group in groups.Values)
        {
            if (group.AlreadyInLibrary)
                continue;
            var best = PickBestExecutable(group);
            if (best is not null)
                candidates.Add(new ScanCandidate(group.DisplayName, best));
        }

        return candidates
            .OrderBy(c => c.SuggestedName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Maps an executable to its game folder (immediate child of the scan root) + display name.</summary>
    private static (string GameDir, string DisplayName) ResolveGameDir(string root, string exePath)
    {
        var rel = Path.GetRelativePath(root, exePath);
        var sep = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        if (sep < 0)
        {
            // Executable sits directly in the scanned root.
            var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return (root, CleanName(rootName));
        }
        var first = rel[..sep];
        return (Path.Combine(root, first), CleanName(first));
    }

    /// <summary>Picks the most game-like executable: a name match to the folder wins, else the largest binary.</summary>
    private static string? PickBestExecutable(ScanGroup group)
    {
        if (group.Executables.Count == 0)
            return null;

        // Prefer non-junk executables; only fall back to junk if that's all there is.
        var pool = group.Executables.Where(e => !e.IsJunk).ToList();
        if (pool.Count == 0)
            pool = group.Executables;

        // Among the pool, an executable whose name covers the game folder's words wins
        // (term = folder, candidate = exe name), e.g. "Dispatch-Win64-Shipping" covers "Dispatch".
        var nameMatches = pool
            .Where(e => ArtworkService.Similarity(
                group.DisplayName, Path.GetFileNameWithoutExtension(e.Path)) >= 0.5)
            .ToList();
        var pick = (nameMatches.Count > 0 ? nameMatches : pool)
            .OrderByDescending(e => e.Size)
            .First();
        return pick.Path;
    }

    private static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private sealed class ScanGroup(string displayName)
    {
        public string DisplayName { get; } = displayName;
        public List<ScanExe> Executables { get; } = new();
        public bool AlreadyInLibrary { get; set; }
    }

    private readonly record struct ScanExe(string Path, long Size, bool IsJunk);

    public async Task<IReadOnlyList<Game>> AddScannedGamesAsync(IEnumerable<ScanCandidate> confirmed)
    {
        var added = new List<Game>();
        foreach (var candidate in confirmed)
        {
            try
            {
                added.Add(await AddGameAsync(new AddGameRequest(candidate.SuggestedName, candidate.ExecutablePath)));
            }
            catch (DuplicateExecutableException)
            {
                // Skip anything that snuck in as a duplicate.
            }
        }
        return added;
    }

    private async Task<Dictionary<int, GameStats>> ComputeStatsAsync(MosaicDbContext db)
    {
        var sessions = await db.PlaySessions.AsNoTracking()
            .Where(s => s.EndedAt != null)
            .Select(s => new { s.GameId, s.DurationSeconds, s.EndedAt })
            .ToListAsync();

        return sessions
            .GroupBy(s => s.GameId)
            .ToDictionary(
                g => g.Key,
                g => new GameStats(
                    TimeSpan.FromSeconds(g.Sum(s => s.DurationSeconds ?? 0)),
                    g.Max(s => s.EndedAt),
                    g.Count()));
    }

    /// <summary>Per-game (unlocked, total) achievement counts, for the list/grid progress indicator.</summary>
    private static async Task<Dictionary<int, (int Unlocked, int Total)>> ComputeAchievementCountsAsync(MosaicDbContext db)
    {
        var rows = await db.Achievements.AsNoTracking()
            .Select(a => new { a.GameId, Unlocked = a.UnlockedAt != null })
            .ToListAsync();

        return rows
            .GroupBy(a => a.GameId)
            .ToDictionary(g => g.Key, g => (g.Count(a => a.Unlocked), g.Count()));
    }

    private static GameListItem ToListItem(
        Game game,
        Dictionary<int, GameStats> statsByGame,
        Dictionary<int, (int Unlocked, int Total)> achievementsByGame)
    {
        var stats = statsByGame.TryGetValue(game.Id, out var s) ? s : GameStats.Empty;
        var cover = game.Artwork.FirstOrDefault(a => a.Kind == ArtworkKind.Grid)?.LocalPath;
        var (unlocked, total) = achievementsByGame.TryGetValue(game.Id, out var a) ? a : (0, 0);
        return new GameListItem(game, stats, cover, unlocked, total);
    }

    private void TryDeleteCachedFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && _paths.IsInsideDataDirectory(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Non-fatal: leaving an orphaned cache file is acceptable.
        }
    }

    private async Task FetchArtworkSafelyAsync(int gameId)
    {
        try
        {
            await _artwork.FetchArtworkAsync(gameId);
        }
        catch
        {
            // Artwork is best-effort: swallow so a background fetch can't crash the app. Fetch
            // failures are reported to the UI via the artwork service's ArtworkFetchFailed event,
            // not thrown; this guards only a rare rethrow (e.g. genuine cancellation on shutdown).
        }
    }

    private static bool IsJunkExecutable(string root, string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (JunkNameFragments.Any(f => name.Contains(f)))
            return true;

        // Inspect the folders between the scan root and the executable.
        var rel = Path.GetRelativePath(root, path);
        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => JunkPathSegments.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static string CleanName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw ?? string.Empty;
        var spaced = raw.Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
