using Mosaic.Models;

namespace Mosaic.Services;

public interface IMediaPlaybackTracker
{
    /// <summary>Raised when a media item is opened for watching; argument is the media item id.</summary>
    event EventHandler<int>? WatchStarted;

    /// <summary>Raised after a media item's watched state changes (manually or auto-detected); argument is the item id.</summary>
    event EventHandler<int>? WatchStateChanged;

    /// <summary>
    /// Opens a movie or episode in the configured preferred player (or the OS default association),
    /// records an open watch session, and raises <see cref="WatchStarted"/>. Returns false (recording
    /// nothing) if the file no longer exists.
    /// </summary>
    Task<bool> PlayAsync(int mediaItemId);

    /// <summary>Sets or clears a media item's watched state (the only path that may *clear* it).</summary>
    Task SetWatchedAsync(int mediaItemId, bool watched);

    /// <summary>Marks an episode watched and returns the series' next unwatched episode (the resume target), or null.</summary>
    Task<MediaItem?> MarkWatchedAndAdvanceAsync(int episodeId);

    /// <summary>The next unwatched episode of a series ordered by season then episode, or null if fully watched.</summary>
    Task<MediaItem?> GetResumeEpisodeAsync(int seriesId);
}
