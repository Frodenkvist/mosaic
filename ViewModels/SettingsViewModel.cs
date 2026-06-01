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

    public ObservableCollection<string> ScanFolders { get; } = new();

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

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs, SteamGridDbClient client)
    {
        _settings = settings;
        _dialogs = dialogs;
        _client = client;
        Load();
    }

    public void Load()
    {
        ScanFolders.Clear();
        foreach (var folder in _settings.Current.ScanFolders)
            ScanFolders.Add(folder);
        ApiKey = _settings.Current.SteamGridDbApiKey;
        SteamWebApiKey = _settings.Current.SteamWebApiKey;
        StatusMessage = null;
        KeyStatusMessage = null;
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
