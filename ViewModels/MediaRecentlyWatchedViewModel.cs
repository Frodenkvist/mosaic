using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>The "Recently Watched" tab: top-level media you have watched, most recent first.</summary>
public partial class MediaRecentlyWatchedViewModel : MediaCollectionViewModel
{
    public MediaRecentlyWatchedViewModel(
        IMediaLibrary library,
        IMediaPlaybackTracker tracker,
        IMediaArtworkService artwork,
        IDialogService dialogs,
        ISettingsService settings)
        : base(library, tracker, artwork, dialogs, settings)
    {
    }

    protected override Task<IReadOnlyList<MediaListItem>> LoadItemsAsync() =>
        Library.GetRecentlyWatchedAsync();
}
