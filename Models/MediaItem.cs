namespace Mosaic.Models;

/// <summary>What a <see cref="MediaItem"/> represents.</summary>
public enum MediaKind
{
    /// <summary>A standalone film: a single video file.</summary>
    Movie = 0,

    /// <summary>A TV series: a folder grouping with no file of its own; its <see cref="MediaItem.Episodes"/> are the videos.</summary>
    Series = 1,

    /// <summary>One episode of a series: a single video file with a season and episode number.</summary>
    Episode = 2,
}

/// <summary>
/// A movie, a TV series, or an episode of a series. A single self-referential table:
/// a <see cref="MediaKind.Series"/> has no <see cref="FilePath"/> and owns child
/// <see cref="MediaKind.Episode"/> rows via <see cref="Episodes"/>/<see cref="ParentId"/>;
/// movies and episodes carry the video <see cref="FilePath"/>. Seasons are a grouping
/// attribute (<see cref="SeasonNumber"/>) of a series' episodes, not their own rows.
/// Watched state and resume position are persisted on the row (user/observer-owned);
/// per-series progress and the resume episode are derived from the children.
/// </summary>
public class MediaItem
{
    public int Id { get; set; }

    public MediaKind Kind { get; set; }

    /// <summary>The owning series for an episode; null for a movie or a series.</summary>
    public int? ParentId { get; set; }
    public MediaItem? Parent { get; set; }

    /// <summary>Display title (from metadata when matched, else parsed from the file/folder name).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Release year when one can be parsed/resolved; null otherwise.</summary>
    public int? Year { get; set; }

    /// <summary>Absolute path to the video file. Null for a <see cref="MediaKind.Series"/>.</summary>
    public string? FilePath { get; set; }

    /// <summary>The item's containing folder (the show folder for a series).</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Season number for an episode; null otherwise.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Episode number within its season for an episode; null otherwise.</summary>
    public int? EpisodeNumber { get; set; }

    public DateTimeOffset DateAdded { get; set; }

    /// <summary>When the item was marked watched (UTC), or null while unwatched. Explicit state.</summary>
    public DateTimeOffset? WatchedAt { get; set; }

    /// <summary>
    /// Last known in-file playback position in seconds, recorded by the system-media-controls
    /// observer where the player publishes it. Informational ("left off at …") — Mosaic does not
    /// command an external player to seek.
    /// </summary>
    public double? ResumePositionSeconds { get; set; }

    /// <summary>TMDB id once matched; identifies the entry for poster/metadata refreshes.</summary>
    public int? TmdbId { get; set; }

    /// <summary>Child episodes for a series; empty for movies and episodes.</summary>
    public List<MediaItem> Episodes { get; set; } = new();

    public List<MediaArtwork> Artwork { get; set; } = new();
    public List<WatchSession> WatchSessions { get; set; } = new();

    public bool IsWatched => WatchedAt is not null;
}
