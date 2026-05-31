namespace Mosaic.Models;

/// <summary>
/// A non-Steam game tracked by Mosaic.
/// </summary>
public class Game
{
    public int Id { get; set; }

    /// <summary>Display name shown in the library.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path to the executable Mosaic launches.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional name of the process that represents the actual game when it
    /// differs from the launched executable (e.g. "game.exe" launched via
    /// "launcher.exe"). Used by play tracking. File name only, e.g. "game.exe".
    /// </summary>
    public string? RealExecutableName { get; set; }

    /// <summary>Optional command-line arguments passed on launch.</summary>
    public string? LaunchArguments { get; set; }

    /// <summary>Optional working directory; defaults to the executable's folder.</summary>
    public string? WorkingDirectory { get; set; }

    public DateTimeOffset DateAdded { get; set; }

    public List<PlaySession> Sessions { get; set; } = new();
    public List<Artwork> Artwork { get; set; } = new();
}
