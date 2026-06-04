using Mosaic.Models;

namespace Mosaic.Services;

/// <summary>Whether a scan candidate is a standalone movie or an episode of a series.</summary>
public enum MediaCandidateKind
{
    Movie = 0,
    Episode = 1,
}

/// <summary>
/// A video file found during a media scan, awaiting user confirmation. For an episode,
/// <see cref="SeriesTitle"/> groups it under a series and <see cref="SeasonNumber"/>/
/// <see cref="EpisodeNumber"/> order it.
/// </summary>
public record MediaScanCandidate(
    MediaCandidateKind Kind,
    string Title,
    string FilePath,
    string FolderPath,
    int? Year = null,
    string? SeriesTitle = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

/// <summary>A top-level media item (movie or series) plus derived display data for the grid.</summary>
public record MediaListItem(
    MediaItem Item,
    string? PosterPath,
    int WatchedEpisodes,
    int TotalEpisodes,
    DateTimeOffset? LastWatched)
{
    public bool IsSeries => Item.Kind == MediaKind.Series;

    /// <summary>True for a series that has episodes — drives the "x/y watched" progress indicator.</summary>
    public bool HasEpisodeProgress => IsSeries && TotalEpisodes > 0;

    /// <summary>True when the user has watched this item (a movie marked watched, or a fully-watched series).</summary>
    public bool IsWatched => IsSeries
        ? TotalEpisodes > 0 && WatchedEpisodes >= TotalEpisodes
        : Item.IsWatched;
}
