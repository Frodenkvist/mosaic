using System.IO;
using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>
/// A source of achievement unlock state for a game. Implementations locate and read the files (or,
/// later, databases/APIs) that record which achievements are unlocked. This is the seam that lets
/// future sources (GOG Galaxy, Xbox, …) be added without touching the orchestrating service.
/// </summary>
public interface IAchievementUnlockSource
{
    /// <summary>True when this source could provide unlock data for the game.</summary>
    bool CanHandle(Game game);

    /// <summary>Candidate file paths this source reads, whether or not they currently exist (used to set up watching).</summary>
    IReadOnlyList<string> LocateFiles(Game game);

    /// <summary>Reads the currently-unlocked achievements from whichever candidate files exist, merged.</summary>
    IReadOnlyList<ParsedUnlock> ReadUnlocks(Game game);
}

/// <summary>
/// Reads unlocks from the files written by common Steam emulators (Goldberg/GSE JSON and
/// CODEX/ALI213/SmartSteamEmu INI). Requires the game to be linked to a Steam appid, which is what
/// names the per-user save folders. Candidate locations cover the per-game install folder and the
/// well-known per-user save roots; unrecognized files are ignored by the parser.
/// </summary>
public class SteamEmulatorUnlockSource : IAchievementUnlockSource
{
    public bool CanHandle(Game game) => game.SteamAppId is > 0;

    public IReadOnlyList<string> LocateFiles(Game game)
    {
        if (game.SteamAppId is not int appId || appId <= 0)
            return Array.Empty<string>();

        var appIdStr = appId.ToString();
        var candidates = new List<string>();

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                candidates.Add(path);
        }

        // Per-user save roots written by the various emulators.
        var roaming = SafeFolder(Environment.SpecialFolder.ApplicationData);
        var publicDocs = SafeFolder(Environment.SpecialFolder.CommonDocuments);
        var myDocs = SafeFolder(Environment.SpecialFolder.MyDocuments);

        if (roaming is not null)
        {
            Add(Path.Combine(roaming, "Goldberg SteamEmu Saves", appIdStr, "achievements.json"));
            Add(Path.Combine(roaming, "GSE Saves", appIdStr, "achievements.json"));
            Add(Path.Combine(roaming, "Steam", "CODEX", appIdStr, "achievements.ini"));
            Add(Path.Combine(roaming, "SmartSteamEmu", appIdStr, "stats", "achievements.ini"));
        }
        foreach (var docs in new[] { publicDocs, myDocs })
        {
            if (docs is null)
                continue;
            Add(Path.Combine(docs, "Steam", "CODEX", appIdStr, "achievements.ini"));
            Add(Path.Combine(docs, "Steam", "RUNE", appIdStr, "achievements.ini"));
        }

        // Per-game install folder (emulator dropped next to the executable).
        var gameDir = SafeDir(game.ExecutablePath);
        if (gameDir is not null)
        {
            Add(Path.Combine(gameDir, "steam_settings", "achievements.json"));
            Add(Path.Combine(gameDir, "achievements.json"));
            Add(Path.Combine(gameDir, "SteamEmu", appIdStr, "stats", "achievements.ini"));
            Add(Path.Combine(gameDir, "stats", "achievements.ini"));
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<ParsedUnlock> ReadUnlocks(Game game)
    {
        // Merge across every existing candidate file: an achievement is unlocked if any file says
        // so; prefer the entry that carries a (earliest) timestamp.
        var merged = new Dictionary<string, ParsedUnlock>(StringComparer.Ordinal);

        foreach (var file in LocateFiles(game))
        {
            string text;
            try
            {
                if (!File.Exists(file))
                    continue;
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // file is locked/being written; a later scan or the reconcile pass will catch it
            }

            var parsed = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? EmulatorAchievementParser.ParseGoldbergJson(text)
                : EmulatorAchievementParser.ParseIni(text);

            foreach (var unlock in parsed)
            {
                if (!merged.TryGetValue(unlock.ApiName, out var existing))
                    merged[unlock.ApiName] = unlock;
                else if (existing.UnlockedAt is null && unlock.UnlockedAt is not null)
                    merged[unlock.ApiName] = unlock;
                else if (unlock.UnlockedAt is not null && existing.UnlockedAt is not null
                         && unlock.UnlockedAt < existing.UnlockedAt)
                    merged[unlock.ApiName] = unlock;
            }
        }

        return merged.Values.ToList();
    }

    private static string? SafeFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? SafeDir(string executablePath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(executablePath) ? null : Path.GetDirectoryName(executablePath);
        }
        catch
        {
            return null;
        }
    }
}
