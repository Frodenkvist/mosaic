namespace Mosaic.Models;

/// <summary>Kind of cached image for a media item.</summary>
public enum MediaArtworkKind
{
    /// <summary>Vertical poster / cover (the primary tile image).</summary>
    Poster = 0,

    /// <summary>Wide backdrop / fanart.</summary>
    Backdrop = 1,

    /// <summary>Per-episode still / thumbnail.</summary>
    EpisodeStill = 2,
}

/// <summary>
/// A cached artwork image for a media item. The bytes live on disk at <see cref="LocalPath"/>;
/// the database only stores the reference. Mirrors <see cref="Artwork"/> for the media domain.
/// </summary>
public class MediaArtwork
{
    public int Id { get; set; }

    public int MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }

    public MediaArtworkKind Kind { get; set; }

    /// <summary>Absolute path to the cached image file.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Provider asset id when auto-fetched; null for manual overrides.</summary>
    public long? SourceId { get; set; }

    /// <summary>True when the user supplied this image; auto-fetch must not replace it.</summary>
    public bool IsManualOverride { get; set; }
}
