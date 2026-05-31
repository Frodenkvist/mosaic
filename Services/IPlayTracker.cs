namespace Mosaic.Services;

public interface IPlayTracker
{
    /// <summary>Raised when a game's session starts; argument is the game id.</summary>
    event EventHandler<int>? SessionStarted;

    /// <summary>Raised when a game's session ends; argument is the game id.</summary>
    event EventHandler<int>? SessionEnded;

    /// <summary>When the running session for a game started (UTC), or null if not running.</summary>
    DateTimeOffset? GetRunningSince(int gameId);

    /// <summary>
    /// Launches the game and begins tracking. Records an open session immediately.
    /// Returns false (and records nothing) if the executable no longer exists.
    /// </summary>
    Task<bool> LaunchAsync(int gameId);

    /// <summary>True while a game's tracked process tree is still running.</summary>
    bool IsRunning(int gameId);

    /// <summary>
    /// Closes out any sessions left open by a previous unclean shutdown,
    /// per the conservative discard policy.
    /// </summary>
    Task<int> ReconcileOpenSessionsAsync();
}
