using CommunityToolkit.Mvvm.ComponentModel;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>View of a movie or series for the media grid, with derived watched/progress state.</summary>
public partial class MediaTileViewModel : ObservableObject
{
    private readonly MediaListItem _item;

    public MediaTileViewModel(MediaListItem item)
    {
        _item = item;
    }

    public int MediaItemId => _item.Item.Id;
    public string Title => _item.Item.Title;
    public string? PosterPath => _item.PosterPath;
    public bool HasPoster => !string.IsNullOrWhiteSpace(_item.PosterPath);
    public bool IsSeries => _item.IsSeries;
    public string KindLabel => IsSeries ? "TV Series" : "Movie";
    public string? YearDisplay => _item.Item.Year?.ToString();

    public bool IsWatched => _item.IsWatched;
    public bool HasEpisodeProgress => _item.HasEpisodeProgress;
    public string ProgressDisplay => HasEpisodeProgress ? $"{_item.WatchedEpisodes}/{_item.TotalEpisodes}" : string.Empty;

    /// <summary>"Left off at …" for a partially-watched movie; empty otherwise.</summary>
    public string ResumeDisplay => !IsSeries && !IsWatched ? DisplayFormat.Resume(_item.Item.ResumePositionSeconds) : string.Empty;
    public bool HasResume => ResumeDisplay.Length > 0;

    /// <summary>In-session artwork-fetch state, driving the per-tile fetching/failed indicators.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFetchingArtwork))]
    [NotifyPropertyChangedFor(nameof(ArtworkFetchFailed))]
    private ArtworkFetchStatus _fetchStatus;

    public bool IsFetchingArtwork => FetchStatus == ArtworkFetchStatus.Fetching;
    public bool ArtworkFetchFailed => FetchStatus == ArtworkFetchStatus.Failed;
}
