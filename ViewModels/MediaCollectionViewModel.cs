using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>
/// Shared behaviour for views that show a collection of media tiles: loading,
/// play/resume, removal, and the artwork-fetch badge plumbing. Mirrors
/// <see cref="GameCollectionViewModel"/> for the media domain.
/// </summary>
public abstract partial class MediaCollectionViewModel : ObservableObject
{
    protected readonly IMediaLibrary Library;
    protected readonly IMediaPlaybackTracker Tracker;
    protected readonly IMediaArtworkService Artwork;
    protected readonly IDialogService Dialogs;
    protected readonly ISettingsService Settings;

    // In-session artwork-fetch state by media item id, surviving the tile rebuild a RefreshAsync does.
    private readonly Dictionary<int, ArtworkFetchStatus> _fetchStatus = new();

    public ObservableCollection<MediaTileViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isBusy;

    protected MediaCollectionViewModel(
        IMediaLibrary library,
        IMediaPlaybackTracker tracker,
        IMediaArtworkService artwork,
        IDialogService dialogs,
        ISettingsService settings)
    {
        Library = library;
        Tracker = tracker;
        Artwork = artwork;
        Dialogs = dialogs;
        Settings = settings;

        Tracker.WatchStateChanged += (_, _) => App.RunOnUiAsync(RefreshAsync);
        Artwork.MediaArtworkFetchStarted += (_, id) => App.RunOnUiAsync(() => OnFetchStatusChangedAsync(id, ArtworkFetchStatus.Fetching));
        Artwork.MediaArtworkFetchFailed += (_, id) => App.RunOnUiAsync(() => OnFetchStatusChangedAsync(id, ArtworkFetchStatus.Failed));
        Artwork.MediaArtworkUpdated += (_, id) => App.RunOnUiAsync(() => OnArtworkUpdatedAsync(id));
    }

    /// <summary>Loads the items this collection should display.</summary>
    protected abstract Task<IReadOnlyList<MediaListItem>> LoadItemsAsync();

    public async Task RefreshAsync()
    {
        var items = await LoadItemsAsync();
        Items.Clear();
        foreach (var item in items)
            Items.Add(new MediaTileViewModel(item) { FetchStatus = StatusFor(item.Item.Id) });
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    protected async Task Play(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;

        if (!tile.IsSeries)
        {
            if (!await Tracker.PlayAsync(tile.MediaItemId))
                Dialogs.ShowMessage("Could not play this title; the file may be missing.", "Playback failed");
            return;
        }

        // Series: resume at the next unwatched episode.
        var episode = await Tracker.GetResumeEpisodeAsync(tile.MediaItemId);
        if (episode is null)
        {
            Dialogs.ShowMessage("You've watched every episode of this series.", "All caught up");
            return;
        }
        if (!await Tracker.PlayAsync(episode.Id))
            Dialogs.ShowMessage("Could not play the next episode; the file may be missing.", "Playback failed");
    }

    [RelayCommand]
    protected async Task OpenDetail(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;
        Dialogs.ShowMediaDetail(tile.MediaItemId);
        await RefreshAsync();
    }

    [RelayCommand]
    protected async Task Remove(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;
        var what = tile.IsSeries ? "series (and its episodes)" : "movie";
        if (!Dialogs.Confirm($"Remove \"{tile.Title}\" from your media library? The {what} files on disk are not deleted.",
                "Remove media"))
            return;
        await Library.RemoveAsync(tile.MediaItemId);
        await RefreshAsync();
    }

    [RelayCommand]
    protected async Task RetryArtwork(int mediaItemId)
    {
        SetFetchStatus(mediaItemId, ArtworkFetchStatus.Fetching); // optimistic; events confirm the outcome
        try { await Artwork.FetchArtworkAsync(mediaItemId); }
        catch { /* the service already raised the failed event */ }
    }

    private Task OnArtworkUpdatedAsync(int mediaItemId)
    {
        SetFetchStatus(mediaItemId, ArtworkFetchStatus.None);
        return RefreshAsync();
    }

    private Task OnFetchStatusChangedAsync(int mediaItemId, ArtworkFetchStatus status)
    {
        SetFetchStatus(mediaItemId, status);
        return Task.CompletedTask;
    }

    private void SetFetchStatus(int mediaItemId, ArtworkFetchStatus status)
    {
        if (status == ArtworkFetchStatus.None)
            _fetchStatus.Remove(mediaItemId);
        else
            _fetchStatus[mediaItemId] = status;

        foreach (var tile in Items.Where(t => t.MediaItemId == mediaItemId))
            tile.FetchStatus = status;
    }

    private ArtworkFetchStatus StatusFor(int mediaItemId) =>
        _fetchStatus.TryGetValue(mediaItemId, out var status) ? status : ArtworkFetchStatus.None;
}
