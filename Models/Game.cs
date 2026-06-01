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

    /// <summary>
    /// Steam application id used to resolve achievement definitions; null when the game is not
    /// linked to a Steam schema (e.g. "no Steam achievements" or manual-only).
    /// </summary>
    public int? SteamAppId { get; set; }

    /// <summary>Master switch for achievement tracking on this game.</summary>
    public bool AchievementTrackingEnabled { get; set; } = true;

    /// <summary>How achievements are tracked for this game when tracking is enabled.</summary>
    public AchievementSource AchievementSource { get; set; } = AchievementSource.Auto;

    public List<PlaySession> Sessions { get; set; } = new();
    public List<Artwork> Artwork { get; set; } = new();
    public List<Achievement> Achievements { get; set; } = new();
}
