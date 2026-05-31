using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mosaic.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LibraryViewModel Library { get; }
    public RecentlyPlayedViewModel RecentlyPlayed { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableObject _currentView;

    [ObservableProperty]
    private string _currentSection = "Library";

    public MainViewModel(
        LibraryViewModel library,
        RecentlyPlayedViewModel recentlyPlayed,
        SettingsViewModel settings)
    {
        Library = library;
        RecentlyPlayed = recentlyPlayed;
        Settings = settings;
        _currentView = library;
    }

    /// <summary>Loads initial data once the window is shown.</summary>
    public async Task InitializeAsync()
    {
        await Library.RefreshAsync();
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
