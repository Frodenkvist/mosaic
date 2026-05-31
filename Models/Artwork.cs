namespace Mosaic.Models;

public enum ArtworkKind
{
    /// <summary>Cover / grid image (vertical box-art style).</summary>
    Grid = 0,

    /// <summary>Wide hero / banner image.</summary>
    Hero = 1,

    /// <summary>Transparent logo.</summary>
    Logo = 2,
}

/// <summary>
/// A cached artwork image for a game. The image bytes live on disk at
/// <see cref="LocalPath"/>; the database only stores the reference.
/// </summary>
public class Artwork
{
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public ArtworkKind Kind { get; set; }

    /// <summary>Absolute path to the cached image file.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>SteamGridDB asset id when auto-fetched; null for manual overrides.</summary>
    public long? SourceId { get; set; }

    /// <summary>True when the user supplied this image; auto-fetch must not replace it.</summary>
    public bool IsManualOverride { get; set; }
}
