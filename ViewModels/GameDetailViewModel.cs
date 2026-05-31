using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    private readonly IGameLibrary _library;
    private readonly IPlayTracker _tracker;
    private readonly IArtworkService _artwork;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    private int _gameId;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _executablePath = string.Empty;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private string? _realExecutableName;
    [ObservableProperty] private string? _coverPath;
    [ObservableProperty] private string _playTimeDisplay = "Not played";
    [ObservableProperty] private string _lastPlayedDisplay = "Never played";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Raised when the window hosting this VM should close.</summary>
    public event Action? CloseRequested;

    public GameDetailViewModel(
        IGameLibrary library,
        IPlayTracker tracker,
        IArtworkService artwork,
        IDialogService dialogs,
        ISettingsService settings)
    {
        _library = library;
        _tracker = tracker;
        _artwork = artwork;
        _dialogs = dialogs;
        _settings = settings;
    }

    public async Task InitializeAsync(int gameId)
    {
        _gameId = gameId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var game = await _library.GetGameAsync(_gameId);
        if (game is null)
        {
            CloseRequested?.Invoke();
            return;
        }

        Name = game.Name;
        ExecutablePath = game.ExecutablePath;
        LaunchArguments = game.LaunchArguments;
        WorkingDirectory = game.WorkingDirectory;
        RealExecutableName = game.RealExecutableName;
        CoverPath = game.Artwork.FirstOrDefault(a => a.Kind == ArtworkKind.Grid)?.LocalPath;

        var stats = await _library.GetStatsAsync(_gameId);
        PlayTimeDisplay = DisplayFormat.PlayTime(stats);
        LastPlayedDisplay = DisplayFormat.LastPlayed(stats);
        IsRunning = _tracker.IsRunning(_gameId);
    }

    [RelayCommand]
    private async Task Launch()
    {
        var ok = await _tracker.LaunchAsync(_gameId);
        if (!ok)
        {
            _dialogs.ShowMessage("Could not launch the game; its executable may be missing.", "Launch failed");
            return;
        }
        IsRunning = true;
        StatusMessage = "Launched.";
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var path = _dialogs.PickExecutable();
        if (!string.IsNullOrWhiteSpace(path))
            ExecutablePath = path;
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            await _library.UpdateGameAsync(new Game
            {
                Id = _gameId,
                Name = string.IsNullOrWhiteSpace(Name) ? "Untitled" : Name,
                ExecutablePath = ExecutablePath,
                LaunchArguments = LaunchArguments,
                WorkingDirectory = WorkingDirectory,
                RealExecutableName = RealExecutableName,
            });
            CloseRequested?.Invoke(); // Save & Close
        }
        catch (DuplicateExecutableException)
        {
            _dialogs.ShowMessage("Another game already uses that executable.", "Duplicate game");
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (!_dialogs.Confirm($"Remove \"{Name}\" from your library? The game files are not deleted.", "Remove game"))
            return;
        await _library.RemoveGameAsync(_gameId);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SetCover()
    {
        var image = _dialogs.PickImage();
        if (string.IsNullOrWhiteSpace(image))
            return;
        CoverPath = await _artwork.SetManualOverrideAsync(_gameId, ArtworkKind.Grid, image);
        StatusMessage = "Cover updated.";
    }

    [RelayCommand]
    private async Task RefetchArtwork()
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.SteamGridDbApiKey))
        {
            _dialogs.ShowMessage("Add a SteamGridDB API key in Settings to fetch artwork.", "No API key");
            return;
        }

        StatusMessage = "Fetching artwork…";
        await _artwork.FetchArtworkAsync(_gameId, refetch: true);

        // Reload just the cover; force the image to refresh even if the path is unchanged.
        var game = await _library.GetGameAsync(_gameId);
        var newCover = game?.Artwork.FirstOrDefault(a => a.Kind == ArtworkKind.Grid)?.LocalPath;
        CoverPath = null;
        CoverPath = newCover;
        StatusMessage = string.IsNullOrWhiteSpace(newCover) ? "No artwork match found." : "Artwork refreshed.";
    }
}
