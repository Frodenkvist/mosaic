using Mosaic.Models;

namespace Mosaic.Services;

public interface IMediaLibrary
{
    /// <summary>All top-level media (movies + series) with derived poster/progress, ordered by title.</summary>
    Task<IReadOnlyList<MediaListItem>> GetLibraryAsync();

    /// <summary>Top-level media that has been watched, most-recently-watched first.</summary>
    Task<IReadOnlyList<MediaListItem>> GetRecentlyWatchedAsync();

    /// <summary>A single media item with its artwork loaded, or null.</summary>
    Task<MediaItem?> GetMediaItemAsync(int id);

    /// <summary>A series' episodes ordered by season then episode number.</summary>
    Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(int seriesId);

    /// <summary>
    /// Recursively scans the given media folders for video files not already in the library,
    /// returning candidate movies/episodes for the user to confirm. Never adds anything.
    /// </summary>
    Task<IReadOnlyList<MediaScanCandidate>> ScanFoldersAsync(
        IEnumerable<string> folders, long minFileSizeBytes = MediaLibrary.DefaultMinFileSizeBytes);

    /// <summary>Adds the user-confirmed candidates, grouping episodes under their series.</summary>
    Task<IReadOnlyList<MediaItem>> AddConfirmedAsync(IEnumerable<MediaScanCandidate> confirmed);

    /// <summary>Updates a media item's editable metadata (title, year, season/episode numbers).</summary>
    Task UpdateMediaItemAsync(MediaItem item);

    /// <summary>Removes a media item (cascading a series' episodes/sessions/artwork) and deletes its cached images only.</summary>
    Task RemoveAsync(int id);
}
