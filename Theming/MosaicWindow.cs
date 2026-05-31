using System.Windows;
using System.Windows.Controls;

namespace Mosaic.Theming;

/// <summary>
/// Base window with a custom dark title bar (logo + themed caption buttons),
/// styled by the implicit Style in Themes/MosaicWindow.xaml.
/// </summary>
public class MosaicWindow : Window
{
    /// <summary>Whether the minimize/maximize caption buttons are shown (off for fixed dialogs).</summary>
    public static readonly DependencyProperty ShowCaptionMinMaxProperty =
        DependencyProperty.Register(nameof(ShowCaptionMinMax), typeof(bool), typeof(MosaicWindow),
            new PropertyMetadata(true));

    public bool ShowCaptionMinMax
    {
        get => (bool)GetValue(ShowCaptionMinMaxProperty);
        set => SetValue(ShowCaptionMinMaxProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Minimize") is Button min)
            min.Click += (_, _) => WindowState = WindowState.Minimized;
        if (GetTemplateChild("PART_Maximize") is Button max)
            max.Click += (_, _) => ToggleMaximize();
        if (GetTemplateChild("PART_Close") is Button close)
            close.Click += (_, _) => Close();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
