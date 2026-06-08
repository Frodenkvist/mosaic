using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mosaic.Services;

/// <summary>A single unlocked achievement read from an emulator file: its key and unlock time (if known).</summary>
public record ParsedUnlock(string ApiName, DateTimeOffset? UnlockedAt);

/// <summary>
/// Parses the achievement files written by common Steam emulators into the set of *unlocked*
/// achievements. Two formats are supported: the Goldberg/GSE JSON map and the CODEX/ALI213-style
/// INI. Anything it doesn't recognize yields an empty result rather than throwing, so an unknown
/// file is simply ignored. Only unlocked entries are returned — locked state is the absence of an
/// entry, which keeps unlocks monotonic.
/// </summary>
public static class EmulatorAchievementParser
{
    /// <summary>
    /// Parses the Goldberg/GSE save format: a JSON object mapping each achievement key to
    /// <c>{ "earned": true, "earned_time": &lt;unix-seconds&gt; }</c>. The schema config file
    /// (a JSON array of definitions) is not this format and yields an empty result.
    /// </summary>
    public static IReadOnlyList<ParsedUnlock> ParseGoldbergJson(string json) =>
        ParseGoldbergJson(json, out _);

    /// <summary>
    /// As <see cref="ParseGoldbergJson(string)"/>, but reports a parse <paramref name="error"/> when
    /// the text is present but not valid JSON, so an unreadable/unknown file can be distinguished
    /// from one that is simply absent or has nothing earned. Besides the canonical
    /// <c>key -&gt; { earned, earned_time }</c> map, a flatter <c>key -&gt; true|1|"1"</c> map (no
    /// nested object, no timestamp) is also accepted, and <c>unlock_time</c> is honored as an alias
    /// for <c>earned_time</c>.
    /// </summary>
    public static IReadOnlyList<ParsedUnlock> ParseGoldbergJson(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ParsedUnlock>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<ParsedUnlock>();

            var result = new List<ParsedUnlock>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Flat map variant: key -> true / 1 / "1" (earned, no timestamp).
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    if (IsTrue(prop.Value))
                        result.Add(new ParsedUnlock(prop.Name, null));
                    continue;
                }

                if (!prop.Value.TryGetProperty("earned", out var earned) || !IsTrue(earned))
                    continue;

                DateTimeOffset? when = null;
                if ((prop.Value.TryGetProperty("earned_time", out var t)
                        || prop.Value.TryGetProperty("unlock_time", out t))
                    && t.ValueKind == JsonValueKind.Number
                    && t.TryGetInt64(out var unix) && unix > 0)
                {
                    when = DateTimeOffset.FromUnixTimeSeconds(unix);
                }
                result.Add(new ParsedUnlock(prop.Name, when));
            }
            return result;
        }
        catch (JsonException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return Array.Empty<ParsedUnlock>();
        }
    }

    /// <summary>
    /// Parses CODEX/ALI213/SmartSteamEmu-style INI. Two layouts are recognized:
    /// a per-achievement section with <c>Achieved=1</c> (and an optional <c>UnlockTime=</c>/<c>Time=</c>),
    /// and a flat <c>[Achievements]</c> section of <c>KEY=1</c> lines.
    /// </summary>
    public static IReadOnlyList<ParsedUnlock> ParseIni(string text) => ParseIni(text, out _);

    /// <summary>
    /// As <see cref="ParseIni(string)"/>, but reports a parse <paramref name="error"/> (the INI
    /// grammar is lenient, so this is rarely set). Per-achievement sections are recognized by an
    /// <c>Achieved</c>/<c>HaveAchieved</c>/<c>State</c>/<c>Earned</c> truthy flag, and the unlock
    /// time is read from <c>UnlockTime</c>/<c>Time</c>/<c>HaveAchievedTime</c>/<c>earned_time</c>.
    /// </summary>
    public static IReadOnlyList<ParsedUnlock> ParseIni(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ParsedUnlock>();

        var sections = ReadSections(text);
        var result = new List<ParsedUnlock>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (section, entries) in sections)
        {
            if (IsFlatAchievementsSection(section))
            {
                // A flat [Achievements] section, in one of two shapes:
                //   KEY=1                                        (SmartSteamEmu / simple)
                //   "key" = {unlocked = true, time = 1700000000} (ALI213/3DM Lua-table SteamData)
                foreach (var (rawKey, value) in entries)
                {
                    var key = Unquote(rawKey);
                    if (IsMetaKey(key))
                        continue;

                    var v = value.Trim();
                    bool flatAchieved;
                    DateTimeOffset? flatWhen = null;
                    if (v.StartsWith('{'))
                    {
                        flatAchieved = LuaFlag(v, "unlocked") ?? LuaFlag(v, "achieved") ?? false;
                        if (TryLuaLong(v, "time", out var flatUnix) && flatUnix > 0)
                            flatWhen = DateTimeOffset.FromUnixTimeSeconds(flatUnix);
                    }
                    else
                    {
                        flatAchieved = IsAchievedValue(v);
                    }

                    if (flatAchieved && seen.Add(key))
                        result.Add(new ParsedUnlock(key, flatWhen));
                }
                continue;
            }

            // Per-achievement section: the section name is the achievement key. Different emulators
            // spell the "is unlocked" flag differently.
            if (!(TryGet(entries, "Achieved", out var achieved)
                    || TryGet(entries, "HaveAchieved", out achieved)
                    || TryGet(entries, "State", out achieved)
                    || TryGet(entries, "Earned", out achieved))
                || !IsAchievedValue(achieved))
                continue;

            DateTimeOffset? when = null;
            if ((TryGet(entries, "UnlockTime", out var ut) || TryGet(entries, "Time", out ut)
                    || TryGet(entries, "HaveAchievedTime", out ut) || TryGet(entries, "earned_time", out ut))
                && long.TryParse(ut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
            {
                when = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            if (seen.Add(section))
                result.Add(new ParsedUnlock(section, when));
        }

        return result;
    }

    private static bool IsTrue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => e.TryGetInt64(out var n) && n != 0,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) ? b : e.GetString() == "1",
        _ => false,
    };

    private static bool IsAchievedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim();
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlatAchievementsSection(string section) =>
        section.Equals("Achievements", StringComparison.OrdinalIgnoreCase)
        || section.Equals("ACHIEVE_DATA", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetaKey(string key) =>
        key.Equals("Count", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips surrounding single/double quotes and whitespace from an INI key.</summary>
    private static string Unquote(string s)
    {
        var t = s.Trim();
        if (t.Length >= 2 && (t[0] == '"' || t[0] == '\'') && t[^1] == t[0])
            t = t[1..^1];
        return t.Trim();
    }

    /// <summary>Reads a Lua-table boolean field, e.g. <c>unlocked = true</c> / <c>unlocked = 1</c>; null if absent.</summary>
    private static bool? LuaFlag(string table, string name)
    {
        var m = Regex.Match(table, name + @"\s*=\s*(true|false|1|0)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        var v = m.Groups[1].Value;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    /// <summary>Reads a Lua-table integer field, e.g. <c>time = 1700000000</c>.</summary>
    private static bool TryLuaLong(string table, string name, out long value)
    {
        var m = Regex.Match(table, name + @"\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;
        value = 0;
        return false;
    }

    private static bool TryGet(List<(string Key, string Value)> entries, string key, out string value)
    {
        foreach (var (k, v) in entries)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static List<(string Section, List<(string Key, string Value)> Entries)> ReadSections(string text)
    {
        var sections = new List<(string, List<(string, string)>)>();
        var current = "";
        var entries = new List<(string, string)>();
        void Flush()
        {
            if (current.Length > 0 || entries.Count > 0)
                sections.Add((current, entries));
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                current = line[1..^1].Trim();
                entries = new List<(string, string)>();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            entries.Add((line[..eq].Trim(), line[(eq + 1)..].Trim()));
        }
        Flush();
        return sections;
    }
}
