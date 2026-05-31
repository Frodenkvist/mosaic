using CommunityToolkit.Mvvm.ComponentModel;
using Mosaic.Services;

namespace Mosaic.ViewModels;

/// <summary>View of a game for the library/recently-played lists, with live running state.</summary>
public partial class GameTileViewModel : ObservableObject
{
    private readonly GameListItem _item;

    public GameTileViewModel(GameListItem item)
    {
        _item = item;
    }

    public int GameId => _item.Game.Id;
    public string Name => _item.Game.Name;
    public string? CoverPath => _item.CoverPath;
    public bool HasCover => !string.IsNullOrWhiteSpace(_item.CoverPath);
    public string PlayTimeDisplay => DisplayFormat.PlayTime(_item.Stats);
    public string LastPlayedDisplay => DisplayFormat.LastPlayed(_item.Stats);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    private bool _isRunning;

    /// <summary>UTC time the current session started, when running.</summary>
    public DateTimeOffset? RunningSince { get; set; }

    [ObservableProperty]
    private string _liveElapsedDisplay = string.Empty;

    public bool CanLaunch => !IsRunning;

    /// <summary>Recomputes the live elapsed label from <see cref="RunningSince"/>.</summary>
    public void Tick()
    {
        if (!IsRunning || RunningSince is null)
        {
            LiveElapsedDisplay = string.Empty;
            return;
        }
        var elapsed = DateTimeOffset.UtcNow - RunningSince.Value;
        LiveElapsedDisplay = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
