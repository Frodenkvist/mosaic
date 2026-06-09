namespace Mosaic.Services;

/// <summary>
/// A transparent, click-through, topmost surface drawn over a running game that shows
/// achievement-unlock toasts. Disposing it removes the overlay. Implemented by a WPF window
/// (<c>Views.AchievementOverlayWindow</c>); abstracted here so the overlay service can be
/// unit-tested without a real window.
/// </summary>
public interface IAchievementOverlay : IDisposable
{
    /// <summary>Shows (or replaces) the achievement toast and restarts its auto-dismiss timer.</summary>
    void ShowToast(string title, string subtitle, string? iconPath);
}

/// <summary>Creates and shows a new <see cref="IAchievementOverlay"/>. Must be called on the UI thread.</summary>
public interface IAchievementOverlayFactory
{
    IAchievementOverlay Create();
}

/// <summary>Plays the short "achievement unlocked" sound. Abstracted so the overlay service is testable.</summary>
public interface IAchievementSoundPlayer
{
    void Play();
}
