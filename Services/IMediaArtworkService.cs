using Mosaic.Models;

namespace Mosaic.Services;

public interface IMediaArtworkService
{
    /// <summary>Raised after a media item's artwork (or auto-adopted title/metadata) changes; argument is the item id.</summary>
    event EventHandler<int>? MediaArtworkUpdated;

    /// <summary>Raised when a fetch is actually attempted for an item (a key is configured and there is art to fetch).</summary>
    event EventHandler<int>? MediaArtworkFetchStarted;

    /// <summary>Raised when an attempted fetch produced no usable result, leaving a placeholder.</summary>
    event EventHandler<int>? MediaArtworkFetchFailed;

    /// <summary>
    /// Best-effort fetch of poster/backdrop (and, for a series, per-episode metadata) from TMDB.
    /// No-ops when no API key is configured. Never replaces a manual override.
    /// </summary>
    Task FetchArtworkAsync(int mediaItemId, bool refetch = false, CancellationToken cancellationToken = default);

    /// <summary>Sets a local image as the item's artwork for the given kind (a manual override). Returns the cached path.</summary>
    Task<string> SetManualOverrideAsync(int mediaItemId, MediaArtworkKind kind, string sourceImagePath);

    /// <summary>Fetches missing artwork for every movie/series lacking it (throttled).</summary>
    Task FetchMissingForAllAsync(CancellationToken cancellationToken = default);
}
