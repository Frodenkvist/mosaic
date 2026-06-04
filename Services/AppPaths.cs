using System.IO;

namespace Mosaic.Services;

/// <summary>
/// Resolves and ensures Mosaic's local data directories under %LOCALAPPDATA%\Mosaic.
/// </summary>
public class AppPaths
{
    public string RootDirectory { get; }
    public string ArtworkDirectory { get; }
    public string AchievementsDirectory { get; }
    public string MediaArtworkDirectory { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }

    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mosaic"))
    {
    }

    /// <summary>Roots all data under an explicit directory (used by tests to avoid the real data dir).</summary>
    internal AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        ArtworkDirectory = Path.Combine(RootDirectory, "artwork");
        AchievementsDirectory = Path.Combine(RootDirectory, "achievements");
        MediaArtworkDirectory = Path.Combine(RootDirectory, "media-artwork");
        DatabasePath = Path.Combine(RootDirectory, "mosaic.db");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    /// <summary>Creates the data, artwork, achievement and media-artwork directories if they do not yet exist.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ArtworkDirectory);
        Directory.CreateDirectory(AchievementsDirectory);
        Directory.CreateDirectory(MediaArtworkDirectory);
    }

    /// <summary>True when the given path lives inside Mosaic's own data directory.</summary>
    public bool IsInsideDataDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(RootDirectory);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
