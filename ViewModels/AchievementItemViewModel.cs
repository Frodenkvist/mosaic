using CommunityToolkit.Mvvm.ComponentModel;
using Mosaic.Models;

namespace Mosaic.ViewModels;

/// <summary>One achievement row in the game detail view, with hidden-until-unlocked masking.</summary>
public partial class AchievementItemViewModel : ObservableObject
{
    private readonly string _displayName;
    private readonly string? _description;
    private readonly string? _iconUnlockedPath;
    private readonly string? _iconLockedPath;
    private readonly bool _hidden;

    public AchievementItemViewModel(Achievement achievement)
    {
        Id = achievement.Id;
        _displayName = achievement.DisplayName;
        _description = achievement.Description;
        _iconUnlockedPath = achievement.IconUnlockedPath;
        _iconLockedPath = achievement.IconLockedPath;
        _hidden = achievement.Hidden;
        _isUnlocked = achievement.IsUnlocked;
        _unlockedAt = achievement.UnlockedAt;
    }

    public int Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(IconPath))]
    [NotifyPropertyChangedFor(nameof(UnlockedAtDisplay))]
    private bool _isUnlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockedAtDisplay))]
    private DateTimeOffset? _unlockedAt;

    /// <summary>Hidden, still-locked achievements are masked so the view doesn't spoil them.</summary>
    public string DisplayName => _hidden && !IsUnlocked ? "Hidden achievement" : _displayName;

    public string? Description => _hidden && !IsUnlocked ? "Reach this to reveal what it is." : _description;

    public string? IconPath => IsUnlocked ? _iconUnlockedPath : _iconLockedPath ?? _iconUnlockedPath;

    public string UnlockedAtDisplay => IsUnlocked && UnlockedAt is { } when
        ? when.ToLocalTime().ToString("d MMM yyyy, HH:mm")
        : string.Empty;
}
