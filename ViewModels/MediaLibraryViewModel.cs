using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class MediaLibraryViewModel : MediaCollectionViewModel
{
    public string[] SortOptions { get; } = { "Title (A–Z)", "Recently watched", "Recently added" };

    [ObservableProperty]
    private string _selectedSort = "Title (A–Z)";

    [ObservableProperty]
    private string _searchText = string.Empty;

    public MediaLibraryViewModel(
        IMediaLibrary library,
        IMediaPlaybackTracker tracker,
        IMediaArtworkService artwork,
        IDialogService dialogs,
        ISettingsService settings)
        : base(library, tracker, artwork, dialogs, settings)
    {
    }

    partial void OnSelectedSortChanged(string value) => _ = RefreshAsync();
    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    protected override async Task<IReadOnlyList<MediaListItem>> LoadItemsAsync()
    {
        var items = await Library.GetLibraryAsync();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            items = items
                .Where(i => i.Item.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return SelectedSort switch
        {
            "Recently watched" => items.OrderByDescending(i => i.LastWatched ?? DateTimeOffset.MinValue).ToList(),
            "Recently added" => items.OrderByDescending(i => i.Item.DateAdded).ToList(),
            _ => items.OrderBy(i => i.Item.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
    }

    [RelayCommand]
    private async Task ScanMedia()
    {
        IsBusy = true;
        try
        {
            var folders = Settings.Current.MediaFolders;
            if (folders.Count == 0)
            {
                Dialogs.ShowMessage("No media folders configured. Add folders in Settings.", "Scan for media");
                return;
            }

            var candidates = await Library.ScanFoldersAsync(folders);
            if (candidates.Count == 0)
            {
                Dialogs.ShowMessage("No new movies or episodes found in your media folders.", "Scan for media");
                return;
            }

            var confirmed = Dialogs.ShowMediaScanResults(candidates);
            if (confirmed is null || confirmed.Count == 0)
                return;

            await Library.AddConfirmedAsync(confirmed);
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
        if (string.IsNullOrWhiteSpace(Settings.Current.TmdbApiKey))
        {
            Dialogs.ShowMessage("Add a TMDB API key in Settings to fetch posters.", "No API key");
            return;
        }

        IsBusy = true;
        try
        {
            await Artwork.FetchMissingForAllAsync();
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
