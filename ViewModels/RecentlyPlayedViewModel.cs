using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class RecentlyPlayedViewModel : GameCollectionViewModel
{
    public RecentlyPlayedViewModel(
        IGameLibrary library, IPlayTracker tracker, IDialogService dialogs, IArtworkService artwork)
        : base(library, tracker, dialogs, artwork)
    {
    }

    protected override Task<IReadOnlyList<GameListItem>> LoadItemsAsync() =>
        Library.GetRecentlyPlayedAsync();
}
