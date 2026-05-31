using Mosaic.Models;

namespace Mosaic.Services;

public interface IArtworkService
{
    /// <summary>Raised after a game's artwork (or auto-adopted name) changes; argument is the game id.</summary>
    event EventHandler<int>? ArtworkUpdated;

    /// <summary>
    /// Best-effort fetch of grid/hero/logo artwork from SteamGridDB for a game.
    /// No-ops when no API key is configured. Never replaces a manual override.
    /// Cached artwork is reused rather than re-downloaded, unless <paramref name="refetch"/>
    /// is true, in which case non-override artwork is re-resolved and re-downloaded.
    /// </summary>
    Task FetchArtworkAsync(int gameId, bool refetch = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a local image as the game's artwork for the given kind, marking it a
    /// manual override that auto-fetch will not replace. Returns the cached path.
    /// </summary>
    Task<string> SetManualOverrideAsync(int gameId, ArtworkKind kind, string sourceImagePath);

    /// <summary>
    /// Fetches any missing artwork for every game in the library (throttled). Games that
    /// already have complete artwork are skipped; raises <see cref="ArtworkUpdated"/> per game.
    /// </summary>
    Task FetchMissingForAllAsync(CancellationToken cancellationToken = default);
}
