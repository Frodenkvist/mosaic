namespace Mosaic.Models;

/// <summary>How Mosaic tracks a game's achievements.</summary>
public enum AchievementSource
{
    /// <summary>Resolve definitions from Steam and auto-detect unlocks from emulator files.</summary>
    Auto = 0,

    /// <summary>Manual only: the user marks achievements; no automatic file detection.</summary>
    Manual = 1,

    /// <summary>No achievement tracking for this game.</summary>
    Disabled = 2,
}

/// <summary>
/// One achievement for a game: the definition (from the Steam schema or user-defined) together
/// with its unlock state. Definition and unlock columns are independent — a schema refresh upserts
/// the definition fields keyed by (<see cref="GameId"/>, <see cref="ApiName"/>) and never clears an
/// existing unlock (unlocks are monotonic). Icon bytes live on disk; only the paths are stored.
/// </summary>
public class Achievement
{
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// Stable per-game key (the Steam achievement "name"); identifies the row across schema
    /// refreshes and matches the keys written by Steam emulators. For a user-defined achievement
    /// this is a generated key.
    /// </summary>
    public string ApiName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Absolute path to the cached "unlocked" icon, or null.</summary>
    public string? IconUnlockedPath { get; set; }

    /// <summary>Absolute path to the cached "locked"/grey icon, or null.</summary>
    public string? IconLockedPath { get; set; }

    /// <summary>Hidden/spoiler achievement: name and description are masked until unlocked.</summary>
    public bool Hidden { get; set; }

    /// <summary>Sort order from the schema (or insertion order for a manual definition).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>True when the user defined this achievement by hand (no Steam schema).</summary>
    public bool IsManualDefinition { get; set; }

    /// <summary>When the achievement was unlocked (UTC), or null while still locked.</summary>
    public DateTimeOffset? UnlockedAt { get; set; }

    /// <summary>True when the unlock was set by the user rather than detected from a file.</summary>
    public bool IsManualUnlock { get; set; }

    public bool IsUnlocked => UnlockedAt is not null;
}
