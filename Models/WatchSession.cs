namespace Mosaic.Models;

/// <summary>
/// A record that a media item was opened for watching. Drives recency / "continue watching".
/// <see cref="EndedAt"/> is best-effort (an external player rarely reports when it closes) and
/// is not required for correctness.
/// </summary>
public class WatchSession
{
    public int Id { get; set; }

    public int MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while open; set best-effort if the launch's lifetime is observable.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}
