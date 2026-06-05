using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LibraryViewModel Library { get; }
    public RecentlyPlayedViewModel RecentlyPlayed { get; }
    public MediaLibraryViewModel Media { get; }
    public MediaRecentlyWatchedViewModel MediaRecentlyWatched { get; }
    public SettingsViewModel Settings { get; }

    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _toastTimer;

    // Guards against stacking multiple update prompts (e.g. a startup check and a manual one).
    private bool _updatePromptShowing;

    [ObservableProperty]
    private ObservableObject _currentView;

    [ObservableProperty]
    private string _currentSection = "Library";

    // Live "achievement unlocked" toast, shown briefly when a game unlocks one while running.
    [ObservableProperty]
    private bool _achievementToastVisible;

    [ObservableProperty]
    private string _achievementToastTitle = string.Empty;

    [ObservableProperty]
    private string _achievementToastSubtitle = string.Empty;

    [ObservableProperty]
    private string? _achievementToastIcon;

    public MainViewModel(
        LibraryViewModel library,
        RecentlyPlayedViewModel recentlyPlayed,
        MediaLibraryViewModel media,
        MediaRecentlyWatchedViewModel mediaRecentlyWatched,
        SettingsViewModel settings,
        IAchievementService achievements,
        IUpdateService updates,
        IDialogService dialogs)
    {
        Library = library;
        RecentlyPlayed = recentlyPlayed;
        Media = media;
        MediaRecentlyWatched = mediaRecentlyWatched;
        Settings = settings;
        _updates = updates;
        _dialogs = dialogs;
        _currentView = library;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); AchievementToastVisible = false; };

        // Raised on a background thread; marshal to the UI before touching observable state.
        achievements.AchievementUnlocked += (_, e) => App.RunOnUiAsync(() => ShowAchievementToastAsync(e));
        _updates.UpdateAvailable += (_, info) => App.RunOnUiAsync(() => PromptForUpdateAsync(info));
    }

    /// <summary>
    /// Asks the user whether to install an available update; on consent downloads, verifies, and
    /// applies it, then closes the app so the installer can replace its files (it relaunches us).
    /// </summary>
    private async Task PromptForUpdateAsync(UpdateInfo info)
    {
        if (_updatePromptShowing)
            return;
        _updatePromptShowing = true;
        try
        {
            var update = _dialogs.Confirm(
                $"Mosaic {info.Version} is available (you have {AppEnvironment.CurrentVersion.ToString(3)}).\n\n" +
                "Update now? Mosaic will close, install the update, and reopen.\n" +
                "Choose No to be reminded later.",
                "Update available");
            if (!update)
                return;

            var result = await _updates.DownloadAndApplyAsync(info);
            if (result.Success)
                Application.Current?.Shutdown();
            else
                _dialogs.ShowMessage(result.Message ?? "The update could not be completed.", "Update");
        }
        finally
        {
            _updatePromptShowing = false;
        }
    }

    /// <summary>Loads initial data once the window is shown.</summary>
    public async Task InitializeAsync()
    {
        await Library.RefreshAsync();
    }

    private Task ShowAchievementToastAsync(AchievementUnlockedEventArgs e)
    {
        AchievementToastTitle = e.AchievementName;
        AchievementToastSubtitle = $"Unlocked in {e.GameName}";
        AchievementToastIcon = e.IconPath;
        AchievementToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void DismissAchievementToast()
    {
        _toastTimer.Stop();
        AchievementToastVisible = false;
    }

    [RelayCommand]
    private async Task ShowLibrary()
    {
        CurrentSection = "Library";
        CurrentView = Library;
        await Library.RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowRecentlyPlayed()
    {
        CurrentSection = "Recently Played";
        CurrentView = RecentlyPlayed;
        await RecentlyPlayed.RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowMedia()
    {
        CurrentSection = "Media";
        CurrentView = Media;
        await Media.RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowMediaRecentlyWatched()
    {
        CurrentSection = "Recently Watched";
        CurrentView = MediaRecentlyWatched;
        await MediaRecentlyWatched.RefreshAsync();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentSection = "Settings";
        CurrentView = Settings;
        Settings.Load();
    }
}
