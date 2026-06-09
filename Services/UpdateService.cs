using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mosaic.Services;

/// <summary>
/// Consumes the project's GitHub Releases to discover and apply Mosaic updates by reusing the
/// standard installer (a silent in-place upgrade). Best-effort throughout: any network, parsing,
/// or I/O failure is swallowed and surfaced only as a result status, never crashing the app.
/// </summary>
public partial class UpdateService : IUpdateService
{
    // Public-repo Releases endpoint; no API key required (unauthenticated, ~60 req/hr/IP).
    private const string LatestReleaseUrl = "https://api.github.com/repos/Frodenkvist/mosaic/releases/latest";
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;

    public UpdateService(HttpClient http, AppPaths paths, ISettingsService settings)
    {
        _http = http;
        _paths = paths;
        _settings = settings;
    }

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public UpdateCheckResult? LastResult { get; private set; }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken = default)
    {
        // Gate 1: only installed builds update themselves (dev runs / portable copies are inert).
        if (!AppEnvironment.IsInstalledBuild)
            return Remember(new UpdateCheckResult(UpdateCheckStatus.NotInstalledBuild,
                Message: "Updates are managed by the installer; this is not an installed build."));

        // Gate 2: for an automatic (non-forced) check, honor the preference and the daily throttle.
        if (!force)
        {
            if (!_settings.Current.AutomaticUpdatesEnabled)
                return Remember(new UpdateCheckResult(UpdateCheckStatus.Skipped));

            var last = _settings.Current.LastUpdateCheckUtc;
            if (last is { } t && DateTime.UtcNow - t < ThrottleWindow)
                return Remember(new UpdateCheckResult(UpdateCheckStatus.Skipped));
        }

        UpdateCheckResult result;
        try
        {
            result = await QueryLatestAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new UpdateCheckResult(UpdateCheckStatus.Failed, Message: "Could not check for updates.");
        }

        // Advance the daily throttle only for an *automatic* check that leaves nothing for the user
        // to act on. Two deliberate exclusions:
        //   • A forced, user-initiated check never touches this clock — otherwise clicking "Check
        //     for updates" would push the window forward and suppress the next startup check for
        //     24h, making auto-update-on-startup look broken.
        //   • When an update IS available we also leave the clock untouched, so the next launch
        //     re-checks and re-prompts. A declined ("remind me later") update should resurface on
        //     the next launch rather than disappear for a day.
        if (!force && result.Status != UpdateCheckStatus.UpdateAvailable)
            await TouchLastCheckAsync();

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update is { } info)
            UpdateAvailable?.Invoke(this, info);

        return Remember(result);
    }

    private async Task<UpdateCheckResult> QueryLatestAsync(CancellationToken ct)
    {
        var release = await _http.GetFromJsonAsync<ReleaseDto>(LatestReleaseUrl, ct);
        if (release is null)
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: "Could not check for updates.");

        if (!TryGetNewerVersion(release.TagName, AppEnvironment.CurrentVersion, out var remote))
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

        var installer = release.Assets?.FirstOrDefault(IsInstallerAsset);
        if (installer?.Name is null || string.IsNullOrWhiteSpace(installer.Url))
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate); // newer tag but no usable asset → ignore

        var checksum = release.Assets?
            .FirstOrDefault(a => string.Equals(a.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

        var info = new UpdateInfo(remote, installer.Url, installer.Name, checksum?.Url);
        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info);
    }

    public async Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!AppEnvironment.IsInstalledBuild)
            return new UpdateApplyResult(false, "Updates are managed by the installer; this is not an installed build.");

        // Integrity is verified against a published checksum; without one we cannot vouch for the
        // download (builds are unsigned), so we refuse rather than run an unverified installer.
        if (string.IsNullOrWhiteSpace(update.ChecksumUrl))
            return new UpdateApplyResult(false, "The update could not be verified (no checksum was published).");

        var destPath = Path.Combine(_paths.RootDirectory, update.InstallerFileName);
        try
        {
            if (!await DownloadToFileAsync(update.InstallerUrl, destPath, cancellationToken))
                return new UpdateApplyResult(false, "The update could not be downloaded.");

            var expected = ExtractSha256(await _http.GetStringAsync(update.ChecksumUrl, cancellationToken));
            var actual = await ComputeSha256Async(destPath, cancellationToken);

            if (expected is null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(destPath);
                return new UpdateApplyResult(false, "The update could not be verified and was discarded.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(destPath);
            return new UpdateApplyResult(false, "The update could not be completed.");
        }

        try
        {
            // Silent in-place upgrade; /RESTARTMOSAIC tells the installer to relaunch us afterward.
            // Detached (UseShellExecute) so it outlives this process during the file swap.
            Process.Start(new ProcessStartInfo
            {
                FileName = destPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTMOSAIC",
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            return new UpdateApplyResult(false, "The update installer could not be launched.");
        }

        // Caller closes the app so the installer can replace its files.
        return new UpdateApplyResult(true);
    }

    private async Task<bool> DownloadToFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return false;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destPath);
        await source.CopyToAsync(file, ct);
        return true;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash); // uppercase hex
    }

    /// <summary>Pulls the first 64-hex-character SHA-256 token out of a checksum file's text.</summary>
    private static string? ExtractSha256(string content)
    {
        var match = Sha256Regex().Match(content ?? string.Empty);
        return match.Success ? match.Value : null;
    }

    private async Task TouchLastCheckAsync()
    {
        try
        {
            _settings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
            await _settings.SaveAsync();
        }
        catch
        {
            // Persisting the throttle timestamp is best-effort; failing it must not break the check.
        }
    }

    private UpdateCheckResult Remember(UpdateCheckResult result)
    {
        LastResult = result;
        return result;
    }

    private static bool IsInstallerAsset(AssetDto a) =>
        a.Name is { } name
        && name.StartsWith("MosaicSetup", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single source of truth for "should this release update us?": true when
    /// <paramref name="tag"/> parses to a version strictly newer than <paramref name="running"/>,
    /// yielding the parsed <paramref name="remote"/> version. An unparseable tag is "not newer".
    /// </summary>
    internal static bool TryGetNewerVersion(string? tag, Version running, out Version remote) =>
        TryParseTag(tag, out remote) && Normalize(remote) > Normalize(running);

    /// <summary>Parses a release tag like <c>v1.2.0</c> or <c>1.2.0</c> into a <see cref="Version"/>.</summary>
    internal static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        var trimmed = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out version!);
    }

    /// <summary>
    /// Normalizes to Major.Minor.Build (unspecified components become 0) so comparisons aren't
    /// thrown off by <see cref="Version"/> treating an absent revision as -1 (e.g. <c>1.0.0</c>
    /// vs <c>1.0.0.0</c> would otherwise compare unequal).
    /// </summary>
    internal static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [GeneratedRegex("[A-Fa-f0-9]{64}")]
    private static partial Regex Sha256Regex();

    private class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
    }
}
