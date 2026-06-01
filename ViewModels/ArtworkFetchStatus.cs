namespace Mosaic.ViewModels;

/// <summary>
/// In-session state of a game's artwork/name auto-fetch, surfaced on its library tile.
/// Success is implicit (the art/name update), so there is no explicit "succeeded" state —
/// a finished fetch returns to <see cref="None"/>.
/// </summary>
public enum ArtworkFetchStatus
{
    None,
    Fetching,
    Failed,
}
