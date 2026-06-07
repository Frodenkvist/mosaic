using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>One episode row in the series detail view.</summary>
public partial class EpisodeRowViewModel : ObservableObject
{
    public EpisodeRowViewModel(MediaItem episode)
    {
        EpisodeId = episode.Id;
        SeasonNumber = episode.SeasonNumber ?? 0;
        EpisodeNumber = episode.EpisodeNumber ?? 0;
        Title = episode.Title;
        StillPath = episode.Artwork.FirstOrDefault(a => a.Kind == MediaArtworkKind.EpisodeStill)?.LocalPath;
        ResumeDisplay = episode.WatchedAt is null ? DisplayFormat.Resume(episode.ResumePositionSeconds) : string.Empty;
        _isWatched = episode.WatchedAt is not null;
    }

    public int EpisodeId { get; }
    public int SeasonNumber { get; }
    public int EpisodeNumber { get; }
    public string Title { get; }
    public string? StillPath { get; }
    public bool HasStill => !string.IsNullOrWhiteSpace(StillPath);
    public string EpisodeLabel => $"S{SeasonNumber:D2}E{EpisodeNumber:D2}";
    public string ResumeDisplay { get; }
    public bool HasResume => ResumeDisplay.Length > 0;
    public string WatchLabel => IsWatched ? "✓ Watched" : "Mark watched";

    [ObservableProperty]
    private bool _isWatched;

    // Inline-edit state for correcting an episode's season/episode number and title.
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editSeason = string.Empty;
    [ObservableProperty] private string _editEpisode = string.Empty;

    /// <summary>Seeds the edit fields from the current values and opens the inline editor.</summary>
    public void BeginEdit()
    {
        EditTitle = Title;
        EditSeason = SeasonNumber.ToString();
        EditEpisode = EpisodeNumber.ToString();
        IsEditing = true;
    }

    public void CancelEdit() => IsEditing = false;
}

/// <summary>A season grouping of episodes in the series detail view.</summary>
public class SeasonGroupViewModel
{
    public SeasonGroupViewModel(int seasonNumber, IEnumerable<EpisodeRowViewModel> episodes)
    {
        SeasonNumber = seasonNumber;
        Header = seasonNumber > 0 ? $"Season {seasonNumber}" : "Specials";
        Episodes = new ObservableCollection<EpisodeRowViewModel>(episodes);
    }

    public int SeasonNumber { get; }
    public string Header { get; }
    public ObservableCollection<EpisodeRowViewModel> Episodes { get; }
}

public partial class MediaDetailViewModel : ObservableObject
{
    private readonly IMediaLibrary _library;
    private readonly IMediaPlaybackTracker _tracker;
    private readonly IMediaArtworkService _artwork;
    private readonly IDialogService _dialogs;

    private int _id;

    public event Action? CloseRequested;

    public MediaDetailViewModel(
        IMediaLibrary library,
        IMediaPlaybackTracker tracker,
        IMediaArtworkService artwork,
        IDialogService dialogs)
    {
        _library = library;
        _tracker = tracker;
        _artwork = artwork;
        _dialogs = dialogs;

        _artwork.MediaArtworkUpdated += (_, id) => App.RunOnUiAsync(() => OnArtworkUpdatedAsync(id));
    }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string? _editYear;
    [ObservableProperty] private string? _posterPath;
    [ObservableProperty] private bool _isSeries;
    [ObservableProperty] private bool _isMovie;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MovieWatchLabel))]
    private bool _isWatched;

    public string MovieWatchLabel => IsWatched ? "✓ Watched — click to unmark" : "Mark watched";

    [ObservableProperty] private string _resumeDisplay = string.Empty;
    [ObservableProperty] private string _progressDisplay = string.Empty;
    [ObservableProperty] private bool _hasProgress;
    [ObservableProperty] private string _nextUpDisplay = string.Empty;

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath);

    partial void OnPosterPathChanged(string? value) => OnPropertyChanged(nameof(HasPoster));

    public ObservableCollection<SeasonGroupViewModel> Seasons { get; } = new();

    public async Task InitializeAsync(int mediaItemId)
    {
        _id = mediaItemId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var item = await _library.GetMediaItemAsync(_id);
        if (item is null)
            return;

        Title = item.Title;
        EditYear = item.Year?.ToString();
        PosterPath = item.Artwork.FirstOrDefault(a => a.Kind == MediaArtworkKind.Poster)?.LocalPath;
        IsSeries = item.Kind == MediaKind.Series;
        IsMovie = item.Kind == MediaKind.Movie;
        IsWatched = item.WatchedAt is not null;
        ResumeDisplay = IsMovie && !IsWatched ? DisplayFormat.Resume(item.ResumePositionSeconds) : string.Empty;

        Seasons.Clear();
        if (IsSeries)
        {
            var episodes = await _library.GetEpisodesAsync(_id);
            var rows = episodes.Select(e => new EpisodeRowViewModel(e)).ToList();
            foreach (var group in rows.GroupBy(e => e.SeasonNumber).OrderBy(g => g.Key))
                Seasons.Add(new SeasonGroupViewModel(group.Key, group.OrderBy(e => e.EpisodeNumber)));

            var total = rows.Count;
            var watched = rows.Count(e => e.IsWatched);
            HasProgress = total > 0;
            ProgressDisplay = total > 0 ? $"{watched}/{total} watched" : string.Empty;

            var resume = await _tracker.GetResumeEpisodeAsync(_id);
            NextUpDisplay = resume is not null
                ? $"Next up: S{resume.SeasonNumber ?? 0:D2}E{resume.EpisodeNumber ?? 0:D2} · {resume.Title}"
                : "All episodes watched";
        }
        else
        {
            HasProgress = false;
            ProgressDisplay = string.Empty;
            NextUpDisplay = string.Empty;
        }
    }

    [RelayCommand]
    private async Task PlayMovie()
    {
        if (!await _tracker.PlayAsync(_id))
            _dialogs.ShowMessage("Could not play this title; the file may be missing.", "Playback failed");
    }

    [RelayCommand]
    private async Task ToggleWatchedMovie()
    {
        await _tracker.SetWatchedAsync(_id, !IsWatched);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ResumeSeries()
    {
        var episode = await _tracker.GetResumeEpisodeAsync(_id);
        if (episode is null)
        {
            _dialogs.ShowMessage("You've watched every episode of this series.", "All caught up");
            return;
        }
        if (!await _tracker.PlayAsync(episode.Id))
            _dialogs.ShowMessage("Could not play the next episode; the file may be missing.", "Playback failed");
    }

    [RelayCommand]
    private async Task PlayEpisode(EpisodeRowViewModel? episode)
    {
        if (episode is null)
            return;
        if (!await _tracker.PlayAsync(episode.EpisodeId))
            _dialogs.ShowMessage("Could not play this episode; the file may be missing.", "Playback failed");
    }

    [RelayCommand]
    private async Task ToggleEpisodeWatched(EpisodeRowViewModel? episode)
    {
        if (episode is null)
            return;
        await _tracker.SetWatchedAsync(episode.EpisodeId, !episode.IsWatched);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkWatchedAndPlayNext(EpisodeRowViewModel? episode)
    {
        if (episode is null)
            return;
        var next = await _tracker.MarkWatchedAndAdvanceAsync(episode.EpisodeId);
        await LoadAsync();
        if (next is not null)
            await _tracker.PlayAsync(next.Id);
    }

    [RelayCommand]
    private void BeginEditEpisode(EpisodeRowViewModel? episode) => episode?.BeginEdit();

    [RelayCommand]
    private void CancelEditEpisode(EpisodeRowViewModel? episode) => episode?.CancelEdit();

    [RelayCommand]
    private async Task SaveEpisode(EpisodeRowViewModel? episode)
    {
        if (episode is null)
            return;
        if (!int.TryParse(episode.EditSeason, out var season) || season < 0)
        {
            _dialogs.ShowMessage("Season must be a whole number (0 or greater).", "Invalid season");
            return;
        }
        if (!int.TryParse(episode.EditEpisode, out var number) || number < 0)
        {
            _dialogs.ShowMessage("Episode must be a whole number (0 or greater).", "Invalid episode");
            return;
        }

        await _library.UpdateMediaItemAsync(new MediaItem
        {
            Id = episode.EpisodeId,
            Title = string.IsNullOrWhiteSpace(episode.EditTitle) ? episode.Title : episode.EditTitle.Trim(),
            SeasonNumber = season,
            EpisodeNumber = number,
        });
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveEpisode(EpisodeRowViewModel? episode)
    {
        if (episode is null)
            return;
        if (!_dialogs.Confirm(
                $"Remove \"{episode.EpisodeLabel} · {episode.Title}\" from this series? The file on disk is not deleted.",
                "Remove episode"))
            return;
        await _library.RemoveAsync(episode.EpisodeId);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SetPoster()
    {
        var image = _dialogs.PickImage();
        if (string.IsNullOrWhiteSpace(image))
            return;
        await _artwork.SetManualOverrideAsync(_id, MediaArtworkKind.Poster, image);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshMetadata()
    {
        try { await _artwork.FetchArtworkAsync(_id, refetch: true); }
        catch { /* failure surfaced via the artwork event */ }
    }

    [RelayCommand]
    private async Task Save()
    {
        int? year = int.TryParse(EditYear, out var y) ? y : null;
        await _library.UpdateMediaItemAsync(new MediaItem { Id = _id, Title = Title, Year = year });
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (!_dialogs.Confirm($"Remove \"{Title}\" from your media library? The files on disk are not deleted.",
                "Remove media"))
            return;
        await _library.RemoveAsync(_id);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private Task OnArtworkUpdatedAsync(int mediaItemId)
    {
        if (mediaItemId == _id)
            return LoadAsync();
        return Task.CompletedTask;
    }
}
