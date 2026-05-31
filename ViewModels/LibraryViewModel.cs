using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class LibraryViewModel : GameCollectionViewModel
{
    private readonly ISettingsService _settings;

    public string[] SortOptions { get; } =
    {
        "Name (A–Z)", "Most played", "Recently played", "Recently added",
    };

    [ObservableProperty]
    private string _selectedSort = "Name (A–Z)";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public LibraryViewModel(
        IGameLibrary library,
        IPlayTracker tracker,
        IDialogService dialogs,
        IArtworkService artwork,
        ISettingsService settings)
        : base(library, tracker, dialogs, artwork)
    {
        _settings = settings;
    }

    partial void OnSelectedSortChanged(string value) => _ = RefreshAsync();
    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    protected override async Task<IReadOnlyList<GameListItem>> LoadItemsAsync()
    {
        var items = await Library.GetLibraryAsync();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            items = items
                .Where(i => i.Game.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return SelectedSort switch
        {
            "Most played" => items.OrderByDescending(i => i.Stats.TotalPlayTime).ToList(),
            "Recently played" => items
                .OrderByDescending(i => i.Stats.LastPlayed ?? DateTimeOffset.MinValue).ToList(),
            "Recently added" => items.OrderByDescending(i => i.Game.DateAdded).ToList(),
            _ => items.OrderBy(i => i.Game.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
    }

    [RelayCommand]
    private async Task AddGame()
    {
        var request = Dialogs.ShowAddGame();
        if (request is null)
            return;

        try
        {
            await Library.AddGameAsync(request);
            await RefreshAsync();
        }
        catch (DuplicateExecutableException)
        {
            Dialogs.ShowMessage("That executable is already in your library.", "Duplicate game");
        }
    }

    [RelayCommand]
    private async Task ScanFolders()
    {
        IsBusy = true;
        try
        {
            var folders = _settings.Current.ScanFolders;
            if (folders.Count == 0)
            {
                Dialogs.ShowMessage(
                    "No scan folders configured. Add folders to scan in Settings.", "Scan for games");
                return;
            }

            var candidates = await Library.ScanFoldersAsync(folders);
            if (candidates.Count == 0)
            {
                Dialogs.ShowMessage("No new games found in your scan folders.", "Scan for games");
                return;
            }

            var confirmed = Dialogs.ShowScanResults(candidates);
            if (confirmed is null || confirmed.Count == 0)
                return;

            await Library.AddScannedGamesAsync(confirmed);
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
        if (string.IsNullOrWhiteSpace(_settings.Current.SteamGridDbApiKey))
        {
            Dialogs.ShowMessage("Add a SteamGridDB API key in Settings to fetch artwork.", "No API key");
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

    [RelayCommand]
    private async Task Edit(GameTileViewModel? tile) => await OpenDetail(tile);

    [RelayCommand]
    private async Task Remove(GameTileViewModel? tile)
    {
        if (tile is null)
            return;
        if (!Dialogs.Confirm($"Remove \"{tile.Name}\" from your library? The game files are not deleted.",
                "Remove game"))
            return;
        await Library.RemoveGameAsync(tile.GameId);
        await RefreshAsync();
    }
}
