using Mosaic.Services;

namespace Mosaic.Views;

/// <summary>Creates and shows the real WPF overlay window. Resolved as <see cref="IAchievementOverlayFactory"/>.</summary>
public class AchievementOverlayFactory : IAchievementOverlayFactory
{
    public IAchievementOverlay Create()
    {
        var window = new AchievementOverlayWindow();
        window.Show();   // ShowActivated=false + WS_EX_NOACTIVATE: appears without taking focus
        return window;
    }
}
