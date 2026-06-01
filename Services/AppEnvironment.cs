using System.IO;
using System.Reflection;

namespace Mosaic.Services;

/// <summary>
/// Facts about how this Mosaic process is running — its version and whether it is an
/// installed build. Auto-update only acts on installed builds (see <see cref="IsInstalledBuild"/>);
/// a development run (<c>dotnet run</c>) or an unpacked copy is intentionally update-incapable.
/// </summary>
public static class AppEnvironment
{
    /// <summary>
    /// The running build's version (from the entry assembly), or <c>0.0.0</c> if it can't be read.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// True when this process is an installed build, detected by the presence of the Inno Setup
    /// uninstaller (<c>unins000.exe</c>) alongside the executable. The installer always drops it
    /// into the install directory, so it is a reliable, location-independent "installed" marker
    /// (the install path is user-changeable, so a hard-coded path check would be fragile).
    /// </summary>
    public static bool IsInstalledBuild
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
            }
            catch
            {
                return false;
            }
        }
    }
}
