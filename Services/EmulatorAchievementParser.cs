using System.Globalization;
using System.Text.Json;

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
    public static IReadOnlyList<ParsedUnlock> ParseGoldbergJson(string json)
    {
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
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!prop.Value.TryGetProperty("earned", out var earned) || !IsTrue(earned))
                    continue;

                DateTimeOffset? when = null;
                if (prop.Value.TryGetProperty("earned_time", out var t) && t.ValueKind == JsonValueKind.Number
                    && t.TryGetInt64(out var unix) && unix > 0)
                {
                    when = DateTimeOffset.FromUnixTimeSeconds(unix);
                }
                result.Add(new ParsedUnlock(prop.Name, when));
            }
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<ParsedUnlock>();
        }
    }

    /// <summary>
    /// Parses CODEX/ALI213/SmartSteamEmu-style INI. Two layouts are recognized:
    /// a per-achievement section with <c>Achieved=1</c> (and an optional <c>UnlockTime=</c>/<c>Time=</c>),
    /// and a flat <c>[Achievements]</c> section of <c>KEY=1</c> lines.
    /// </summary>
    public static IReadOnlyList<ParsedUnlock> ParseIni(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ParsedUnlock>();

        var sections = ReadSections(text);
        var result = new List<ParsedUnlock>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (section, entries) in sections)
        {
            if (IsFlatAchievementsSection(section))
            {
                // [Achievements] with KEY=1 lines (one row per achievement).
                foreach (var (key, value) in entries)
                {
                    if (IsMetaKey(key) || !IsAchievedValue(value))
                        continue;
                    if (seen.Add(key))
                        result.Add(new ParsedUnlock(key, null));
                }
                continue;
            }

            // Per-achievement section: the section name is the achievement key.
            if (!TryGet(entries, "Achieved", out var achieved) || !IsAchievedValue(achieved))
                continue;

            DateTimeOffset? when = null;
            if ((TryGet(entries, "UnlockTime", out var ut) || TryGet(entries, "Time", out ut)
                    || TryGet(entries, "earned_time", out ut))
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
