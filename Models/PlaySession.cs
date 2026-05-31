namespace Mosaic.Models;

/// <summary>
/// A single play session for a game: from launch until the whole process tree exits.
/// </summary>
public class PlaySession
{
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game? Game { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the session is in progress.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Recorded duration in seconds; null while in progress.</summary>
    public long? DurationSeconds { get; set; }

    public bool IsOpen => EndedAt is null;
}
