using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>A single media scan candidate the user can confirm, drop, or relabel.</summary>
public partial class MediaScanCandidateViewModel : ObservableObject
{
    private readonly MediaScanCandidate _candidate;

    public MediaScanCandidateViewModel(MediaScanCandidate candidate)
    {
        _candidate = candidate;
        _displayTitle = candidate.SeriesTitle ?? candidate.Title;
    }

    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Editable label: the series name for an episode, otherwise the movie title.</summary>
    [ObservableProperty]
    private string _displayTitle;

    public bool IsEpisode => _candidate.Kind == MediaCandidateKind.Episode;

    public string KindLabel => IsEpisode
        ? $"Episode · S{_candidate.SeasonNumber ?? 0:D2}E{_candidate.EpisodeNumber ?? 0:D2}"
        : _candidate.Year is int y ? $"Movie · {y}" : "Movie";

    public string FilePath => _candidate.FilePath;

    /// <summary>Rebuilds the candidate with the (possibly relabeled) title for the chosen kind.</summary>
    public MediaScanCandidate ToCandidate()
    {
        var label = string.IsNullOrWhiteSpace(DisplayTitle) ? _candidate.Title : DisplayTitle.Trim();
        return IsEpisode
            ? _candidate with { SeriesTitle = label }
            : _candidate with { Title = label };
    }
}

public partial class MediaScanResultsViewModel : ObservableObject
{
    public ObservableCollection<MediaScanCandidateViewModel> Candidates { get; } = new();

    public MediaScanResultsViewModel(IEnumerable<MediaScanCandidate> candidates)
    {
        foreach (var c in candidates)
            Candidates.Add(new MediaScanCandidateViewModel(c));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var c in Candidates)
            c.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var c in Candidates)
            c.IsSelected = false;
    }

    public IReadOnlyList<MediaScanCandidate> GetSelected() =>
        Candidates.Where(c => c.IsSelected).Select(c => c.ToCandidate()).ToList();
}
