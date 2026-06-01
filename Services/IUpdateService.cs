namespace Mosaic.Services;

/// <summary>Outcome categories for an update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>A newer version is available (see <see cref="UpdateCheckResult.Update"/>).</summary>
    UpdateAvailable,

    /// <summary>The running build is already the latest published version.</summary>
    UpToDate,

    /// <summary>The check was skipped because this is not an installed build.</summary>
    NotInstalledBuild,

    /// <summary>The check did not run (automatic checks disabled or throttled).</summary>
    Skipped,

    /// <summary>The check could not be completed (network/parse error, no release, etc.).</summary>
    Failed,
}

/// <summary>An available update: the version and where to get its installer + checksum.</summary>
public record UpdateInfo(Version Version, string InstallerUrl, string InstallerFileName, string? ChecksumUrl);

/// <summary>The result of an update check.</summary>
public record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null, string? Message = null);

/// <summary>The result of downloading and applying an update.</summary>
public record UpdateApplyResult(bool Success, string? Message = null);

/// <summary>
/// Checks for, and applies, Mosaic application updates. All operations are best-effort and only
/// act for installed builds; nothing here downloads or launches anything for a development run.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Raised (on a background thread) when a check finds a newer version. Subscribers must marshal
    /// to the UI thread before touching UI state.
    /// </summary>
    event EventHandler<UpdateInfo>? UpdateAvailable;

    /// <summary>The result of the most recent check, or null if none has run this session.</summary>
    UpdateCheckResult? LastResult { get; }

    /// <summary>
    /// Checks the release source for a newer version. When <paramref name="force"/> is false the
    /// check respects the automatic-update preference and the once-per-day throttle; when true it
    /// runs immediately regardless (but still only for installed builds). Raises
    /// <see cref="UpdateAvailable"/> when an update is found.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the installer for <paramref name="update"/>, verifies it against the published
    /// SHA-256 checksum, and — only if verification passes — launches it to upgrade in place and
    /// relaunch Mosaic. Never executes an unverified installer.
    /// </summary>
    Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
