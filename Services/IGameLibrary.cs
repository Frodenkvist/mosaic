using Mosaic.Models;

namespace Mosaic.Services;

public interface IGameLibrary
{
    /// <summary>All games with derived stats and cover art, ordered by name.</summary>
    Task<IReadOnlyList<GameListItem>> GetLibraryAsync();

    /// <summary>Games that have been played, most-recently-played first.</summary>
    Task<IReadOnlyList<GameListItem>> GetRecentlyPlayedAsync();

    /// <summary>A single game with its sessions and artwork loaded, or null.</summary>
    Task<Game?> GetGameAsync(int id);

    /// <summary>Aggregate play stats for one game.</summary>
    Task<GameStats> GetStatsAsync(int gameId);

    /// <summary>Adds a game. Throws <see cref="DuplicateExecutableException"/> on a duplicate path.</summary>
    Task<Game> AddGameAsync(AddGameRequest request);

    Task UpdateGameAsync(Game game);

    /// <summary>Removes a game and its sessions/artwork. Deletes cached artwork files only.</summary>
    Task RemoveGameAsync(int gameId);

    /// <summary>Scans folders for candidate executables not already in the library.</summary>
    Task<IReadOnlyList<ScanCandidate>> ScanFoldersAsync(IEnumerable<string> folders);

    /// <summary>Adds the user-confirmed scan candidates, skipping duplicates.</summary>
    Task<IReadOnlyList<Game>> AddScannedGamesAsync(IEnumerable<ScanCandidate> confirmed);
}
