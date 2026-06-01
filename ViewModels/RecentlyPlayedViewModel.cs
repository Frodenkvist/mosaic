using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class RecentlyPlayedViewModel : GameCollectionViewModel
{
    public RecentlyPlayedViewModel(
        IGameLibrary library, IPlayTracker tracker, IDialogService dialogs,
        IArtworkService artwork, IAchievementService achievements)
        : base(library, tracker, dialogs, artwork, achievements)
    {
    }

    protected override Task<IReadOnlyList<GameListItem>> LoadItemsAsync() =>
        Library.GetRecentlyPlayedAsync();
}
