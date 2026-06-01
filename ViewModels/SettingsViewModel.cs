using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly SteamGridDbClient _client;
    private readonly IUpdateService _updates;

    // Suppresses the auto-update toggle's auto-save while Load() seeds it from settings.
    private bool _suppressAutoUpdateSave;

    public ObservableCollection<string> ScanFolders { get; } = new();

    /// <summary>The currently installed Mosaic version (e.g. "1.0.0").</summary>
    public string CurrentVersion { get; } = AppEnvironment.CurrentVersion.ToString(3);

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string? _steamWebApiKey;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _keyStatusMessage;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _automaticUpdatesEnabled = true;

    [ObservableProperty]
    private string? _updateStatusMessage;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs, SteamGridDbClient client, IUpdateService updates)
    {
        _settings = settings;
        _dialogs = dialogs;
        _client = client;
        _updates = updates;
        Load();
    }

    public void Load()
    {
        ScanFolders.Clear();
        foreach (var folder in _settings.Current.ScanFolders)
            ScanFolders.Add(folder);
        ApiKey = _settings.Current.SteamGridDbApiKey;
        SteamWebApiKey = _settings.Current.SteamWebApiKey;

        _suppressAutoUpdateSave = true;
        AutomaticUpdatesEnabled = _settings.Current.AutomaticUpdatesEnabled;
        _suppressAutoUpdateSave = false;

        StatusMessage = null;
        KeyStatusMessage = null;
        UpdateStatusMessage = null;
    }

    /// <summary>The automatic-update toggle auto-saves immediately (no Save click needed).</summary>
    partial void OnAutomaticUpdatesEnabledChanged(bool value)
    {
        if (_suppressAutoUpdateSave)
            return;
        _settings.Current.AutomaticUpdatesEnabled = value;
        _ = _settings.SaveAsync();
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingForUpdates = true;
        UpdateStatusMessage = "Checking for updates…";
        try
        {
            // force: true bypasses the daily throttle and the automatic-check preference; finding an
            // update also raises UpdateAvailable, which drives the install prompt (in MainViewModel).
            var result = await _updates.CheckForUpdateAsync(force: true);
            UpdateStatusMessage = result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => $"Update available: Mosaic {result.Update!.Version.ToString(3)}.",
                UpdateCheckStatus.UpToDate => "You’re up to date.",
                UpdateCheckStatus.NotInstalledBuild => "Updates are managed by the installer (not an installed build).",
                _ => result.Message ?? "Couldn’t check for updates.",
            };
        }
        catch
        {
            UpdateStatusMessage = "Couldn’t check for updates.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var folder = _dialogs.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return;
        if (ScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            return;

        ScanFolders.Add(folder);
        await PersistFoldersAsync();   // auto-save: no need to click Save for folders
    }

    [RelayCommand]
    private async Task RemoveFolder()
    {
        if (SelectedFolder is null)
            return;
        ScanFolders.Remove(SelectedFolder);
        await PersistFoldersAsync();
    }

    /// <summary>Persists just the scan-folder list, leaving the API key to the Save button.</summary>
    private async Task PersistFoldersAsync()
    {
        _settings.Current.ScanFolders = ScanFolders.ToList();
        await _settings.SaveAsync();
        StatusMessage = "Scan folders saved.";
    }

    [RelayCommand]
    private async Task TestKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            KeyStatusMessage = "Enter a key to test.";
            return;
        }

        IsTesting = true;
        KeyStatusMessage = "Testing…";
        try
        {
            var ok = await _client.ValidateKeyAsync(ApiKey.Trim(), CancellationToken.None);
            KeyStatusMessage = ok ? "✓ Key is valid." : "✗ Key was rejected.";
        }
        catch
        {
            KeyStatusMessage = "✗ Could not reach SteamGridDB.";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        _settings.Current.ScanFolders = ScanFolders.ToList();
        _settings.Current.SteamGridDbApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
        _settings.Current.SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        await _settings.SaveAsync();
        StatusMessage = "Settings saved.";
    }
}
