using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LibraryViewModel Library { get; }
    public RecentlyPlayedViewModel RecentlyPlayed { get; }
    public SettingsViewModel Settings { get; }

    private readonly DispatcherTimer _toastTimer;

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
        SettingsViewModel settings,
        IAchievementService achievements)
    {
        Library = library;
        RecentlyPlayed = recentlyPlayed;
        Settings = settings;
        _currentView = library;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); AchievementToastVisible = false; };

        // Raised on a background thread; marshal to the UI before touching observable state.
        achievements.AchievementUnlocked += (_, e) => App.RunOnUiAsync(() => ShowAchievementToastAsync(e));
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
    private void ShowSettings()
    {
        CurrentSection = "Settings";
        CurrentView = Settings;
        Settings.Load();
    }
}
