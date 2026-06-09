namespace Mosaic.Services;

/// <summary>
/// Bridges play-session and achievement events onto an in-game overlay and an audio cue. While a
/// game launched through Mosaic is running, a transparent overlay is shown (when enabled); when an
/// achievement unlocks it plays a sound (when enabled) and renders a toast on the overlay.
///
/// Instantiated at startup (like <see cref="SystemMediaWatchObserver"/>) so its subscriptions are
/// wired for the whole app lifetime. Tracker and achievement events fire on background threads, so
/// every handler body is marshalled onto the UI thread via <see cref="App.RunOnUiAsync"/> — which
/// also serialises all access to the mutable session count and overlay reference.
/// </summary>
public class AchievementOverlayService : IDisposable
{
    private readonly IPlayTracker _tracker;
    private readonly IAchievementService _achievements;
    private readonly ISettingsService _settings;
    private readonly IAchievementOverlayFactory _overlayFactory;
    private readonly IAchievementSoundPlayer _sound;

    // Touched only inside UI-thread-marshalled handlers (see class remarks), so no locking needed.
    private int _activeSessions;
    private IAchievementOverlay? _overlay;

    public AchievementOverlayService(
        IPlayTracker tracker,
        IAchievementService achievements,
        ISettingsService settings,
        IAchievementOverlayFactory overlayFactory,
        IAchievementSoundPlayer sound)
    {
        _tracker = tracker;
        _achievements = achievements;
        _settings = settings;
        _overlayFactory = overlayFactory;
        _sound = sound;

        _tracker.SessionStarted += OnSessionStarted;
        _tracker.SessionEnded += OnSessionEnded;
        _achievements.AchievementUnlocked += OnAchievementUnlocked;
    }

    private void OnSessionStarted(object? sender, int gameId) =>
        _ = App.RunOnUiAsync(() => { HandleSessionStarted(); return Task.CompletedTask; });

    private void HandleSessionStarted()
    {
        // Ref-count concurrent sessions; create the overlay only on the 0 -> 1 transition.
        if (++_activeSessions != 1)
            return;
        if (!_settings.Current.GameOverlayEnabled)   // read live: a launch after disabling gets no overlay
            return;
        try
        {
            _overlay = _overlayFactory.Create();
        }
        catch
        {
            _overlay = null;   // overlay creation must never break play tracking
        }
    }

    private void OnSessionEnded(object? sender, int gameId) =>
        _ = App.RunOnUiAsync(() => { HandleSessionEnded(); return Task.CompletedTask; });

    private void HandleSessionEnded()
    {
        if (_activeSessions > 0 && --_activeSessions == 0)
            TearDownOverlay();
    }

    private void OnAchievementUnlocked(object? sender, AchievementUnlockedEventArgs e) =>
        _ = App.RunOnUiAsync(() => { HandleUnlock(e); return Task.CompletedTask; });

    private void HandleUnlock(AchievementUnlockedEventArgs e)
    {
        // Sound is gated only on its own setting and is independent of the visual overlay, so an
        // unlock is never silent even when the overlay can't draw (e.g. exclusive fullscreen).
        if (_settings.Current.AchievementSoundEnabled)
            _sound.Play();
        _overlay?.ShowToast(e.AchievementName, $"Unlocked in {e.GameName}", e.IconPath);
    }

    private void TearDownOverlay()
    {
        try { _overlay?.Dispose(); }
        catch { /* disposing the overlay must never throw into the tracker */ }
        _overlay = null;
    }

    public void Dispose()
    {
        _tracker.SessionStarted -= OnSessionStarted;
        _tracker.SessionEnded -= OnSessionEnded;
        _achievements.AchievementUnlocked -= OnAchievementUnlocked;
        TearDownOverlay();
    }
}
