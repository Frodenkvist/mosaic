using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>
/// Shared behaviour for views that show a collection of game tiles: loading,
/// launching, live running badges and a per-second elapsed timer.
/// </summary>
public abstract partial class GameCollectionViewModel : ObservableObject
{
    protected readonly IGameLibrary Library;
    protected readonly IPlayTracker Tracker;
    protected readonly IDialogService Dialogs;
    protected readonly IArtworkService Artwork;

    private readonly DispatcherTimer _timer;

    public ObservableCollection<GameTileViewModel> Games { get; } = new();

    [ObservableProperty]
    private bool _isEmpty;

    protected GameCollectionViewModel(
        IGameLibrary library, IPlayTracker tracker, IDialogService dialogs, IArtworkService artwork)
    {
        Library = library;
        Tracker = tracker;
        Dialogs = dialogs;
        Artwork = artwork;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => TickRunning();

        Tracker.SessionStarted += (_, id) => App.RunOnUiAsync(() => OnSessionStartedAsync(id));
        Tracker.SessionEnded += (_, _) => App.RunOnUiAsync(RefreshAsync);

        // Artwork/name resolved asynchronously after a game is added; refresh to show it.
        Artwork.ArtworkUpdated += (_, _) => App.RunOnUiAsync(RefreshAsync);
    }

    /// <summary>Loads the items this collection should display.</summary>
    protected abstract Task<IReadOnlyList<GameListItem>> LoadItemsAsync();

    public async Task RefreshAsync()
    {
        var items = await LoadItemsAsync();
        Games.Clear();
        foreach (var item in items)
        {
            var tile = new GameTileViewModel(item);
            ApplyRunningState(tile);
            Games.Add(tile);
        }
        IsEmpty = Games.Count == 0;
        UpdateTimerState();
    }

    private Task OnSessionStartedAsync(int gameId)
    {
        var tile = Games.FirstOrDefault(t => t.GameId == gameId);
        if (tile is not null)
        {
            ApplyRunningState(tile);
            tile.Tick();
        }
        UpdateTimerState();
        return Task.CompletedTask;
    }

    private void ApplyRunningState(GameTileViewModel tile)
    {
        tile.RunningSince = Tracker.GetRunningSince(tile.GameId);
        tile.IsRunning = tile.RunningSince is not null;
    }

    private void TickRunning()
    {
        var anyRunning = false;
        foreach (var tile in Games)
        {
            if (tile.IsRunning)
            {
                tile.Tick();
                anyRunning = true;
            }
        }
        if (!anyRunning)
            _timer.Stop();
    }

    private void UpdateTimerState()
    {
        if (Games.Any(t => t.IsRunning))
        {
            if (!_timer.IsEnabled) _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    [RelayCommand]
    protected async Task Launch(GameTileViewModel? tile)
    {
        if (tile is null || tile.IsRunning)
            return;
        var launched = await Tracker.LaunchAsync(tile.GameId);
        if (!launched)
            Dialogs.ShowMessage("Could not launch the game; its executable may be missing.", "Launch failed");
    }

    [RelayCommand]
    protected async Task OpenDetail(GameTileViewModel? tile)
    {
        if (tile is null)
            return;
        Dialogs.ShowGameDetail(tile.GameId);
        await RefreshAsync();
    }
}
