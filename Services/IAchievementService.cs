using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>Payload for a live achievement unlock, carrying enough to render a notification.</summary>
public class AchievementUnlockedEventArgs : EventArgs
{
    public required int GameId { get; init; }
    public required string GameName { get; init; }
    public required string AchievementName { get; init; }
    public string? IconPath { get; init; }
}

public interface IAchievementService
{
    /// <summary>Raised (on a background thread) when an achievement is detected newly unlocked during play.</summary>
    event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;

    /// <summary>Raised (on a background thread) when a game's achievement list or progress changes; argument is the game id.</summary>
    event EventHandler<int>? AchievementsChanged;

    /// <summary>True when achievement schemas can be auto-resolved (a Steam Web API key is configured).</summary>
    bool IsAutoResolutionAvailable { get; }

    /// <summary>The game's achievements (definitions + unlock state), ordered for display.</summary>
    Task<IReadOnlyList<Achievement>> GetAchievementsAsync(int gameId);

    /// <summary>Unlocked-out-of-total for a game, derived from stored achievements.</summary>
    Task<(int Unlocked, int Total)> GetProgressAsync(int gameId);

    /// <summary>Proposes Steam appid candidates for a game by name match (user confirms before linking).</summary>
    Task<IReadOnlyList<SteamApp>> SuggestAppsAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>Links a game to a Steam appid and resolves its achievement schema (preserving unlock state).</summary>
    Task LinkAppIdAsync(int gameId, int appId, CancellationToken cancellationToken = default);

    /// <summary>Marks a game as having no Steam achievements (clears the appid; manual marking still works).</summary>
    Task SetUnlinkedAsync(int gameId);

    /// <summary>Sets a game's achievement tracking enabled flag and source mode.</summary>
    Task SetSourceAsync(int gameId, bool enabled, AchievementSource source);

    /// <summary>Re-resolves a linked game's achievement definitions from Steam, preserving unlock state.</summary>
    Task RefreshAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans the game's emulator files for unlocks, persists any new ones, and returns a result with
    /// the newly-unlocked achievements plus a <see cref="ScanDiagnostic"/> explaining what was
    /// searched, found, parsed, and matched (so a scan that finds nothing can be explained).
    /// </summary>
    Task<ScanResult> ScanUnlocksAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>Manually sets an achievement's unlocked/locked state.</summary>
    Task SetUnlockedAsync(int gameId, int achievementId, bool unlocked);

    /// <summary>Adds a user-defined achievement (for games with no resolvable schema).</summary>
    Task<Achievement> AddManualAchievementAsync(int gameId, string displayName, string? description = null);
}
