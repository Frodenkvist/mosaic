using System.IO;

namespace Mosaic.Services;

/// <summary>
/// Resolves and ensures Mosaic's local data directories under %LOCALAPPDATA%\Mosaic.
/// </summary>
public class AppPaths
{
    public string RootDirectory { get; }
    public string ArtworkDirectory { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }

    public AppPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootDirectory = Path.Combine(localAppData, "Mosaic");
        ArtworkDirectory = Path.Combine(RootDirectory, "artwork");
        DatabasePath = Path.Combine(RootDirectory, "mosaic.db");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    /// <summary>Creates the data and artwork directories if they do not yet exist.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ArtworkDirectory);
    }

    /// <summary>True when the given path lives inside Mosaic's own data directory.</summary>
    public bool IsInsideDataDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(RootDirectory);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
