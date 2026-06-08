using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>
/// One candidate emulator file considered during an unlock scan, together with what came of it:
/// whether it existed, how many achievement keys were parsed from it, and any read/parse error.
/// Used to make a "nothing detected" result explainable.
/// </summary>
public sealed record ScanCandidateInfo(string Path, bool Existed, int ParsedKeyCount, string? Error);

/// <summary>
/// Diagnostic for a single automatic unlock scan: which candidate files were considered and which
/// existed, how many achievement keys were parsed, how many matched the game's stored definitions,
/// and how many were unmatched. Logged on every scan and surfaced to the user so that a scan which
/// finds no unlocks can be explained rather than silently reporting nothing.
/// </summary>
public sealed record ScanDiagnostic
{
    /// <summary>Every candidate location considered, in the order searched.</summary>
    public IReadOnlyList<ScanCandidateInfo> Candidates { get; init; } = Array.Empty<ScanCandidateInfo>();

    /// <summary>Total achievement keys parsed across all existing files (before matching).</summary>
    public int ParsedKeyCount { get; init; }

    /// <summary>How many parsed keys matched a stored achievement definition.</summary>
    public int MatchedCount { get; init; }

    /// <summary>How many parsed keys matched no stored definition.</summary>
    public int UnmatchedCount { get; init; }

    /// <summary>A small sample of the unmatched keys, for logging and display.</summary>
    public IReadOnlyList<string> SampleUnmatchedKeys { get; init; } = Array.Empty<string>();

    /// <summary>Set when the scan short-circuited before reading files (e.g. unlinked, disabled).</summary>
    public string? Note { get; init; }

    public int LocationsConsidered => Candidates.Count;
    public int LocationsFound => Candidates.Count(c => c.Existed);

    public static ScanDiagnostic Empty { get; } = new();

    /// <summary>A one-line, human-readable explanation suitable for the detail-view status line.</summary>
    public string Summary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Note))
                return Note!;

            var considered = LocationsConsidered;
            var found = LocationsFound;
            if (found == 0)
                return $"No recognized achievement file found (searched {considered} known " +
                       $"location{(considered == 1 ? "" : "s")}). The game may use an unsupported " +
                       "emulator or a save location Mosaic doesn't know about.";

            var parts = new List<string>
            {
                $"Found {found} file{(found == 1 ? "" : "s")} in {considered} searched " +
                    $"location{(considered == 1 ? "" : "s")}",
                $"parsed {ParsedKeyCount} unlock{(ParsedKeyCount == 1 ? "" : "s")}",
                $"{MatchedCount} matched the achievement list",
            };
            if (UnmatchedCount > 0)
                parts.Add($"{UnmatchedCount} key{(UnmatchedCount == 1 ? "" : "s")} matched no definition " +
                          "(try Refresh, or the App ID may be wrong)");
            return string.Join("; ", parts) + ".";
        }
    }
}

/// <summary>The outcome of an unlock scan: the achievements newly unlocked by it plus its diagnostic.</summary>
public sealed record ScanResult(IReadOnlyList<Achievement> NewlyUnlocked, ScanDiagnostic Diagnostic);
