using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    /// <summary>
    /// Reads the currently-unlocked achievements from whichever candidate files exist, merged, and
    /// also reports per-candidate diagnostic info (existed / parsed-key count / read-or-parse error)
    /// so a scan that finds nothing can be explained.
    /// </summary>
    SourceReadResult ReadUnlocks(Game game);
}

/// <summary>The unlocks read from a game's emulator files plus the per-candidate diagnostic info.</summary>
public sealed record SourceReadResult(
    IReadOnlyList<ParsedUnlock> Unlocks,
    IReadOnlyList<ScanCandidateInfo> Candidates,
    string? FoundSaveFolder = null);

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

        // The Goldberg/GSE JSON unlock map lives under an appid-named folder in a per-user save root.
        void AddGoldberg(string? root)
        {
            if (root is null) return;
            Add(Path.Combine(root, "Goldberg SteamEmu Saves", appIdStr, "achievements.json"));
            Add(Path.Combine(root, "GSE Saves", appIdStr, "achievements.json"));
        }

        // CODEX/RUNE/ALI213 and similar drop an INI under <root>\Steam\<emu>\<appid>\.
        void AddSteamEmuInis(string? root)
        {
            if (root is null) return;
            foreach (var emu in new[] { "CODEX", "RUNE", "ALI213", "VOKSI", "TENOKE", "Goldberg" })
            {
                Add(Path.Combine(root, "Steam", emu, appIdStr, "achievements.ini"));
                Add(Path.Combine(root, "Steam", emu, appIdStr, "stats", "achievements.ini"));
            }
        }

        var roaming = SafeFolder(Environment.SpecialFolder.ApplicationData);
        var local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
        var publicDocs = SafeFolder(Environment.SpecialFolder.CommonDocuments);
        var myDocs = SafeFolder(Environment.SpecialFolder.MyDocuments);

        // %AppData% and %LocalAppData%: Goldberg/GSE saves and Steam-emulator INIs.
        foreach (var root in new[] { roaming, local })
        {
            AddGoldberg(root);
            AddSteamEmuInis(root);
            if (root is null) continue;
            Add(Path.Combine(root, "SmartSteamEmu", appIdStr, "stats", "achievements.ini"));
            Add(Path.Combine(root, "EMPRESS", appIdStr, "remote", appIdStr, "achievements.json"));
        }

        // Public/My Documents: CODEX/RUNE/ALI213 INIs and online-fix's own layout.
        foreach (var docs in new[] { publicDocs, myDocs })
        {
            if (docs is null)
                continue;
            AddSteamEmuInis(docs);
            Add(Path.Combine(docs, "OnlineFix", appIdStr, "Stats", "achievements.ini"));
            Add(Path.Combine(docs, "OnlineFix", appIdStr, "achievements.json"));
        }

        // Per-game install folder (the emulator was dropped next to the executable).
        var gameDir = SafeDir(game.ExecutablePath);
        if (gameDir is not null)
        {
            Add(Path.Combine(gameDir, "steam_settings", "achievements.json"));
            Add(Path.Combine(gameDir, "achievements.json"));
            Add(Path.Combine(gameDir, "SteamEmu", appIdStr, "stats", "achievements.ini"));
            Add(Path.Combine(gameDir, "stats", "achievements.ini"));
            Add(Path.Combine(gameDir, "ALI213", "Stats", "achievements.ini"));
            Add(Path.Combine(gameDir, "Stats", "achievements.ini"));
            // ALI213/3DM-style "SteamData" wrapper: a flat [ACHIEVEMENTS] Lua-table INI.
            Add(Path.Combine(gameDir, "SteamData", "user_stats.ini"));
            Add(Path.Combine(gameDir, "SteamData", "Achievements.ini"));

            // Goldberg/gbe_fork can redirect saves via steam_settings\local_save.txt, whose contents
            // are a path (absolute, or relative to the game folder) holding the saves instead.
            foreach (var saveRoot in ResolveLocalSaveRoots(gameDir))
            {
                Add(Path.Combine(saveRoot, appIdStr, "achievements.json"));
                Add(Path.Combine(saveRoot, "achievements.json"));
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads a Goldberg/gbe_fork <c>steam_settings\local_save.txt</c> redirect, if present, and
    /// returns the save root(s) it points at (absolute, or resolved relative to the game folder).
    /// </summary>
    private static IEnumerable<string> ResolveLocalSaveRoots(string gameDir)
    {
        var localSave = Path.Combine(gameDir, "steam_settings", "local_save.txt");
        string rel;
        try
        {
            if (!File.Exists(localSave))
                yield break;
            rel = File.ReadAllText(localSave).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        if (rel.Length == 0)
            yield break;

        var resolved = Path.IsPathRooted(rel) ? rel : Path.Combine(gameDir, rel);
        yield return resolved;
    }

    public SourceReadResult ReadUnlocks(Game game)
    {
        // Merge across every existing candidate file: an achievement is unlocked if any file says
        // so; prefer the entry that carries a (earliest) timestamp. Every candidate considered is
        // recorded — existed, parsed-key count, and any error — for the scan diagnostic.
        var merged = new Dictionary<string, ParsedUnlock>(StringComparer.Ordinal);
        var candidates = new List<ScanCandidateInfo>();
        var gameDir = SafeDir(game.ExecutablePath);
        string? foundSaveFolder = null;

        foreach (var file in LocateFiles(game))
        {
            var existed = false;
            var count = 0;
            string? error = null;

            // Record whether the candidate's containing folder exists. A folder that exists but is not
            // the game's own install folder (which always exists) is evidence the emulator ran or is
            // configured there — used to tell a missing schema from a never-run/unsupported emulator.
            var dir = SafeDir(file);
            var dirExisted = dir is not null && Directory.Exists(dir);
            if (dirExisted && foundSaveFolder is null
                && !(gameDir is not null && string.Equals(dir, gameDir, StringComparison.OrdinalIgnoreCase)))
            {
                foundSaveFolder = dir;
            }

            try
            {
                if (File.Exists(file))
                {
                    existed = true;
                    var text = File.ReadAllText(file);
                    var parsed = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? EmulatorAchievementParser.ParseGoldbergJson(text, out error)
                        : EmulatorAchievementParser.ParseIni(text, out error);
                    count = parsed.Count;

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
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File is locked/being written; a later scan or the reconcile pass will catch it.
                error = ex.GetType().Name;
            }

            candidates.Add(new ScanCandidateInfo(file, existed, count, error) { DirectoryExisted = dirExisted });
        }

        return new SourceReadResult(merged.Values.ToList(), candidates, foundSaveFolder);
    }

    /// <summary>
    /// The path where a generated gbe_fork/Goldberg achievement schema should be written for this game
    /// (<c>&lt;gameDir&gt;\steam_settings\achievements.json</c>), or null when the game folder can't be
    /// resolved from its executable path.
    /// </summary>
    public static string? SchemaTargetPath(Game game)
    {
        var gameDir = SafeDir(game.ExecutablePath);
        return gameDir is null ? null : Path.Combine(gameDir, "steam_settings", "achievements.json");
    }

    /// <summary>
    /// Serializes achievement definitions into the gbe_fork/Goldberg <c>achievements.json</c> schema:
    /// a JSON array of objects with the emulator's exact field names. <c>hidden</c> is emitted as the
    /// string <c>"0"</c>/<c>"1"</c>, and <c>icon</c>/<c>icongray</c> are left empty (Mosaic places no
    /// image files — only the achievement <c>name</c> matters for unlock tracking). Only the key,
    /// display name, description, and hidden flag are needed for the emulator to recognize each
    /// achievement and persist its unlock.
    /// </summary>
    public static string BuildSchemaJson(IEnumerable<Achievement> definitions)
    {
        var entries = definitions.Select(d => new
        {
            name = d.ApiName,
            displayName = d.DisplayName,
            description = d.Description ?? string.Empty,
            hidden = d.Hidden ? "1" : "0",
            icon = string.Empty,
            icongray = string.Empty,
        });
        return JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
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
