using System.Collections.ObjectModel;
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
    private readonly IAchievementService _achievements;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    private int _gameId;
    private bool _loadingAchievementSettings;

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

    // --- Achievements ---
    public ObservableCollection<AchievementItemViewModel> Achievements { get; } = new();

    public AchievementSource[] AchievementSourceOptions { get; } =
        { AchievementSource.Auto, AchievementSource.Manual, AchievementSource.Disabled };

    [ObservableProperty] private string? _steamAppIdText;
    [ObservableProperty] private bool _achievementTrackingEnabled = true;
    [ObservableProperty] private AchievementSource _achievementSource = AchievementSource.Auto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievements))]
    private string _achievementSummary = "No achievements yet.";

    [ObservableProperty] private string? _achievementStatus;
    [ObservableProperty] private string _newAchievementName = string.Empty;

    /// <summary>
    /// True while an achievement action is running. Gates every achievement-mutating command so a
    /// second one can't start (and overlap an in-flight schema fetch / unlock scan) until it finishes.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindAppIdCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAppIdCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlinkCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshAchievementsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanUnlocksCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddManualAchievementCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAchievementCommand))]
    private bool _isAchievementBusy;

    private bool CanRunAchievementAction() => !IsAchievementBusy;

    public bool HasAchievements => Achievements.Count > 0;

    /// <summary>True when achievement schemas can be auto-resolved (a Steam Web API key is set).</summary>
    public bool AchievementAutoAvailable => _achievements.IsAutoResolutionAvailable;

    /// <summary>Raised when the window hosting this VM should close.</summary>
    public event Action? CloseRequested;

    public GameDetailViewModel(
        IGameLibrary library,
        IPlayTracker tracker,
        IArtworkService artwork,
        IAchievementService achievements,
        IDialogService dialogs,
        ISettingsService settings)
    {
        _library = library;
        _tracker = tracker;
        _artwork = artwork;
        _achievements = achievements;
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

        _loadingAchievementSettings = true;
        SteamAppIdText = game.SteamAppId?.ToString();
        AchievementTrackingEnabled = game.AchievementTrackingEnabled;
        AchievementSource = game.AchievementSource;
        _loadingAchievementSettings = false;

        await LoadAchievementsAsync();
    }

    private async Task LoadAchievementsAsync()
    {
        var items = await _achievements.GetAchievementsAsync(_gameId);
        Achievements.Clear();
        foreach (var a in items)
            Achievements.Add(new AchievementItemViewModel(a));

        var unlocked = items.Count(a => a.IsUnlocked);
        AchievementSummary = items.Count == 0
            ? "No achievements yet. Link a Steam App ID or add one manually."
            : $"{unlocked} / {items.Count} unlocked";
        OnPropertyChanged(nameof(HasAchievements));
        OnPropertyChanged(nameof(AchievementAutoAvailable));
    }

    // Apply tracking config changes immediately (but ignore the initial load assignments).
    partial void OnAchievementTrackingEnabledChanged(bool value) => _ = ApplyAchievementSourceAsync();
    partial void OnAchievementSourceChanged(AchievementSource value) => _ = ApplyAchievementSourceAsync();

    private async Task ApplyAchievementSourceAsync()
    {
        if (_loadingAchievementSettings)
            return;
        await _achievements.SetSourceAsync(_gameId, AchievementTrackingEnabled, AchievementSource);
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task FindAppId()
    {
        IsAchievementBusy = true;
        try
        {
            AchievementStatus = "Searching Steam…";
            var candidates = await _achievements.SuggestAppsAsync(_gameId);
            if (candidates.Count == 0)
            {
                AchievementStatus = "No Steam match found. Enter an App ID manually.";
                return;
            }

            // Confirm candidates best-first; the user accepts one or falls back to manual entry.
            foreach (var app in candidates.Take(3))
            {
                if (_dialogs.Confirm($"Link “{Name}” to “{app.Name}” (App ID {app.AppId})?", "Link achievements"))
                {
                    SteamAppIdText = app.AppId.ToString();
                    await LinkAppIdAsync(app.AppId);
                    return;
                }
            }
            AchievementStatus = "No match accepted. Enter an App ID manually if you know it.";
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task ApplyAppId()
    {
        if (!int.TryParse(SteamAppIdText?.Trim(), out var appId) || appId <= 0)
        {
            AchievementStatus = "Enter a numeric Steam App ID.";
            return;
        }
        IsAchievementBusy = true;
        try
        {
            await LinkAppIdAsync(appId);
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    private async Task LinkAppIdAsync(int appId)
    {
        if (!_achievements.IsAutoResolutionAvailable)
        {
            AchievementStatus = "Add a Steam Web API key in Settings to fetch the achievement list.";
            // Still persist the link so it resolves once a key is added.
        }
        else
        {
            AchievementStatus = "Fetching achievements from Steam…";
        }
        await _achievements.LinkAppIdAsync(_gameId, appId);
        await LoadAchievementsAsync();
        AchievementStatus = Achievements.Count > 0
            ? $"Linked. {AchievementSummary}."
            : _achievements.IsAutoResolutionAvailable
                ? "Linked, but this App ID has no achievements."
                : "Linked. Add a Steam Web API key in Settings to fetch the list.";
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task Unlink()
    {
        IsAchievementBusy = true;
        try
        {
            await _achievements.SetUnlinkedAsync(_gameId);
            SteamAppIdText = null;
            await LoadAchievementsAsync();
            AchievementStatus = "Unlinked from Steam achievements.";
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task RefreshAchievements()
    {
        if (!_achievements.IsAutoResolutionAvailable)
        {
            AchievementStatus = "Add a Steam Web API key in Settings to fetch the achievement list.";
            return;
        }
        IsAchievementBusy = true;
        try
        {
            AchievementStatus = "Refreshing achievements…";
            await _achievements.RefreshAsync(_gameId);
            await LoadAchievementsAsync();
            AchievementStatus = "Achievements refreshed.";
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task ScanUnlocks()
    {
        IsAchievementBusy = true;
        try
        {
            AchievementStatus = "Scanning for unlocks…";
            var result = await _achievements.ScanUnlocksAsync(_gameId);
            await LoadAchievementsAsync();
            var count = result.NewlyUnlocked.Count;
            // On a no-result scan, show the diagnostic (why nothing registered) rather than a bare message.
            AchievementStatus = count > 0
                ? $"Found {count} new unlock{(count == 1 ? "" : "s")}."
                : $"No new unlocks. {result.Diagnostic.Summary}";
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task ToggleAchievement(AchievementItemViewModel? item)
    {
        if (item is null)
            return;
        IsAchievementBusy = true;
        try
        {
            var willUnlock = !item.IsUnlocked;
            await _achievements.SetUnlockedAsync(_gameId, item.Id, willUnlock);
            item.IsUnlocked = willUnlock;
            item.UnlockedAt = willUnlock ? DateTimeOffset.UtcNow : null;
            await LoadAchievementsAsync();
        }
        finally
        {
            IsAchievementBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAchievementAction))]
    private async Task AddManualAchievement()
    {
        if (string.IsNullOrWhiteSpace(NewAchievementName))
            return;
        IsAchievementBusy = true;
        try
        {
            await _achievements.AddManualAchievementAsync(_gameId, NewAchievementName.Trim());
            NewAchievementName = string.Empty;
            await LoadAchievementsAsync();
        }
        finally
        {
            IsAchievementBusy = false;
        }
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
