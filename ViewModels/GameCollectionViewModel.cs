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

    // Authoritative, in-session artwork-fetch state keyed by game id. Held here (not on the
    // tiles) so it survives the full tile rebuild a RefreshAsync does — e.g. when one game's
    // fetch succeeds during a batch add, the still-fetching tiles keep their badges.
    private readonly Dictionary<int, ArtworkFetchStatus> _fetchStatus = new();

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

        // Artwork fetch lifecycle, raised on background threads after a game is added:
        // started/failed update just the affected tile; a successful update refreshes to show
        // the new cover/name.
        Artwork.ArtworkFetchStarted += (_, id) => App.RunOnUiAsync(() => OnFetchStatusChangedAsync(id, ArtworkFetchStatus.Fetching));
        Artwork.ArtworkFetchFailed += (_, id) => App.RunOnUiAsync(() => OnFetchStatusChangedAsync(id, ArtworkFetchStatus.Failed));
        Artwork.ArtworkUpdated += (_, id) => App.RunOnUiAsync(() => OnArtworkUpdatedAsync(id));
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
            ApplyFetchStatus(tile);
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

    private Task OnFetchStatusChangedAsync(int gameId, ArtworkFetchStatus status)
    {
        SetFetchStatus(gameId, status);
        return Task.CompletedTask;
    }

    private Task OnArtworkUpdatedAsync(int gameId)
    {
        // Success: the art/name update is the signal, so clear any fetching/failed badge.
        // Clear the tile directly (not just via the map) so the badge goes away even if the
        // subsequent RefreshAsync faults for any reason.
        SetFetchStatus(gameId, ArtworkFetchStatus.None);
        return RefreshAsync();
    }

    /// <summary>Records the fetch state for a game and reflects it on its tile if present.</summary>
    private void SetFetchStatus(int gameId, ArtworkFetchStatus status)
    {
        if (status == ArtworkFetchStatus.None)
            _fetchStatus.Remove(gameId);
        else
            _fetchStatus[gameId] = status;

        var tile = Games.FirstOrDefault(t => t.GameId == gameId);
        if (tile is not null)
            tile.FetchStatus = status;
    }

    private void ApplyRunningState(GameTileViewModel tile)
    {
        tile.RunningSince = Tracker.GetRunningSince(tile.GameId);
        tile.IsRunning = tile.RunningSince is not null;
    }

    private void ApplyFetchStatus(GameTileViewModel tile) =>
        tile.FetchStatus = _fetchStatus.TryGetValue(tile.GameId, out var status)
            ? status
            : ArtworkFetchStatus.None;

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

    /// <summary>Re-attempts the artwork fetch for a game whose fetch previously failed.</summary>
    [RelayCommand]
    protected async Task RetryArtwork(int gameId)
    {
        SetFetchStatus(gameId, ArtworkFetchStatus.Fetching); // optimistic; events confirm the outcome
        try
        {
            await Artwork.FetchArtworkAsync(gameId);
        }
        catch
        {
            // The service already raised ArtworkFetchFailed; swallow so the command doesn't fault.
        }
    }
}
