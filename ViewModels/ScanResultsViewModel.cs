using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class ScanCandidateViewModel : ObservableObject
{
    public ScanCandidate Candidate { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    public ScanCandidateViewModel(ScanCandidate candidate)
    {
        Candidate = candidate;
    }

    public string SuggestedName => Candidate.SuggestedName;
    public string ExecutablePath => Candidate.ExecutablePath;
}

public partial class ScanResultsViewModel : ObservableObject
{
    public ObservableCollection<ScanCandidateViewModel> Candidates { get; } = new();

    public ScanResultsViewModel(IEnumerable<ScanCandidate> candidates)
    {
        foreach (var c in candidates)
            Candidates.Add(new ScanCandidateViewModel(c));
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

    public IReadOnlyList<ScanCandidate> GetSelected() =>
        Candidates.Where(c => c.IsSelected).Select(c => c.Candidate).ToList();
}
