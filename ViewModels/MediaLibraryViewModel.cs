using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class MediaLibraryViewModel : ObservableObject
{
    private readonly IMediaLibrary _library;
    private readonly IMediaPlaybackTracker _tracker;
    private readonly IMediaArtworkService _artwork;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    // In-session artwork-fetch state by media item id, surviving the tile rebuild a RefreshAsync does.
    private readonly Dictionary<int, ArtworkFetchStatus> _fetchStatus = new();

    public ObservableCollection<MediaTileViewModel> Items { get; } = new();

    /// <summary>Recently-watched items, most recent first — the "continue watching" strip.</summary>
    public ObservableCollection<MediaTileViewModel> ContinueWatching { get; } = new();

    public string[] SortOptions { get; } = { "Title (A–Z)", "Recently watched", "Recently added" };

    [ObservableProperty]
    private string _selectedSort = "Title (A–Z)";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasContinueWatching;

    public MediaLibraryViewModel(
        IMediaLibrary library,
        IMediaPlaybackTracker tracker,
        IMediaArtworkService artwork,
        IDialogService dialogs,
        ISettingsService settings)
    {
        _library = library;
        _tracker = tracker;
        _artwork = artwork;
        _dialogs = dialogs;
        _settings = settings;

        _tracker.WatchStateChanged += (_, _) => App.RunOnUiAsync(RefreshAsync);
        _artwork.MediaArtworkFetchStarted += (_, id) => App.RunOnUiAsync(() => SetFetchStatusAsync(id, ArtworkFetchStatus.Fetching));
        _artwork.MediaArtworkFetchFailed += (_, id) => App.RunOnUiAsync(() => SetFetchStatusAsync(id, ArtworkFetchStatus.Failed));
        _artwork.MediaArtworkUpdated += (_, id) => App.RunOnUiAsync(() => OnArtworkUpdatedAsync(id));
    }

    partial void OnSelectedSortChanged(string value) => _ = RefreshAsync();
    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        var items = await _library.GetLibraryAsync();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            items = items
                .Where(i => i.Item.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        items = SelectedSort switch
        {
            "Recently watched" => items.OrderByDescending(i => i.LastWatched ?? DateTimeOffset.MinValue).ToList(),
            "Recently added" => items.OrderByDescending(i => i.Item.DateAdded).ToList(),
            _ => items.OrderBy(i => i.Item.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };

        Items.Clear();
        foreach (var item in items)
        {
            var tile = new MediaTileViewModel(item) { FetchStatus = StatusFor(item.Item.Id) };
            Items.Add(tile);
        }
        IsEmpty = Items.Count == 0;

        var recent = await _library.GetRecentlyWatchedAsync();
        ContinueWatching.Clear();
        foreach (var item in recent.Take(10))
            ContinueWatching.Add(new MediaTileViewModel(item) { FetchStatus = StatusFor(item.Item.Id) });
        HasContinueWatching = ContinueWatching.Count > 0;
    }

    [RelayCommand]
    private async Task Play(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;

        if (!tile.IsSeries)
        {
            if (!await _tracker.PlayAsync(tile.MediaItemId))
                _dialogs.ShowMessage("Could not play this title; the file may be missing.", "Playback failed");
            return;
        }

        // Series: resume at the next unwatched episode.
        var episode = await _tracker.GetResumeEpisodeAsync(tile.MediaItemId);
        if (episode is null)
        {
            _dialogs.ShowMessage("You've watched every episode of this series.", "All caught up");
            return;
        }
        if (!await _tracker.PlayAsync(episode.Id))
            _dialogs.ShowMessage("Could not play the next episode; the file may be missing.", "Playback failed");
    }

    [RelayCommand]
    private async Task OpenDetail(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;
        _dialogs.ShowMediaDetail(tile.MediaItemId);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ScanMedia()
    {
        IsBusy = true;
        try
        {
            var folders = _settings.Current.MediaFolders;
            if (folders.Count == 0)
            {
                _dialogs.ShowMessage("No media folders configured. Add folders in Settings.", "Scan for media");
                return;
            }

            var candidates = await _library.ScanFoldersAsync(folders);
            if (candidates.Count == 0)
            {
                _dialogs.ShowMessage("No new movies or episodes found in your media folders.", "Scan for media");
                return;
            }

            var confirmed = _dialogs.ShowMediaScanResults(candidates);
            if (confirmed is null || confirmed.Count == 0)
                return;

            await _library.AddConfirmedAsync(confirmed);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefetchAllArtwork()
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.TmdbApiKey))
        {
            _dialogs.ShowMessage("Add a TMDB API key in Settings to fetch posters.", "No API key");
            return;
        }

        IsBusy = true;
        try
        {
            await _artwork.FetchMissingForAllAsync();
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Remove(MediaTileViewModel? tile)
    {
        if (tile is null)
            return;
        var what = tile.IsSeries ? "series (and its episodes)" : "movie";
        if (!_dialogs.Confirm($"Remove \"{tile.Title}\" from your media library? The {what} files on disk are not deleted.",
                "Remove media"))
            return;
        await _library.RemoveAsync(tile.MediaItemId);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RetryArtwork(int mediaItemId)
    {
        await SetFetchStatusAsync(mediaItemId, ArtworkFetchStatus.Fetching);
        try { await _artwork.FetchArtworkAsync(mediaItemId); }
        catch { /* the service already raised the failed event */ }
    }

    private Task OnArtworkUpdatedAsync(int mediaItemId)
    {
        SetFetchStatus(mediaItemId, ArtworkFetchStatus.None);
        return RefreshAsync();
    }

    private Task SetFetchStatusAsync(int mediaItemId, ArtworkFetchStatus status)
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
        foreach (var tile in ContinueWatching.Where(t => t.MediaItemId == mediaItemId))
            tile.FetchStatus = status;
    }

    private ArtworkFetchStatus StatusFor(int mediaItemId) =>
        _fetchStatus.TryGetValue(mediaItemId, out var status) ? status : ArtworkFetchStatus.None;
}
