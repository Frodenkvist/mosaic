using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Mosaic.Converters;
using Mosaic.Services;

namespace Mosaic.Views;

/// <summary>
/// A borderless, transparent, click-through, topmost overlay drawn over a running game. It paints
/// nothing while idle (the toast is collapsed) so the player is unaware of it, and surfaces
/// achievement-unlock toasts in a corner of the active monitor. All Win32 interop for the
/// extended window styles, monitor placement, and z-order is isolated to this class (mirroring the
/// interop quarantine in <c>JobObjectTracker</c>).
/// </summary>
public partial class AchievementOverlayWindow : Window, IAchievementOverlay
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(6);
    private const double TopMargin = 36;

    // Reused for graceful (null on missing/invalid) icon loading, identical to the in-app toast.
    private static readonly PathToImageConverter IconConverter = new();

    private readonly DispatcherTimer _hideTimer;

    public AchievementOverlayWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = ToastDuration };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideToast(); };
    }

    /// <summary>Applies the click-through / no-activate / tool-window extended styles once the HWND exists.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public void ShowToast(string title, string subtitle, string? iconPath)
    {
        ToastTitle.Text = title;
        ToastSubtitle.Text = subtitle;
        ToastIcon.Source = IconConverter.Convert(iconPath!, typeof(ImageSource), null!, CultureInfo.InvariantCulture) as ImageSource;

        PositionOnActiveMonitor();
        ReassertTopmost();

        ToastRoot.Visibility = Visibility.Visible;
        ToastRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        ToastSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() });

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideToast()
    {
        var fade = new DoubleAnimation(ToastRoot.Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => ToastRoot.Visibility = Visibility.Collapsed;
        ToastRoot.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Places the window centered horizontally near the top of the monitor that currently hosts the
    /// foreground (game) window, DPI-aware; falls back to the primary monitor's work area on failure.
    /// </summary>
    private void PositionOnActiveMonitor()
    {
        try
        {
            var foreground = GetForegroundWindow();
            var monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var source = PresentationSource.FromVisual(this);
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) && source?.CompositionTarget is not null)
            {
                // Monitor rect is in physical pixels; convert to the DIPs that Window.Left/Top use.
                var toDip = source.CompositionTarget.TransformFromDevice;
                var topLeft = toDip.Transform(new Point(info.rcWork.left, info.rcWork.top));
                var bottomRight = toDip.Transform(new Point(info.rcWork.right, info.rcWork.bottom));
                Left = topLeft.X + ((bottomRight.X - topLeft.X) - Width) / 2;
                Top = topLeft.Y + TopMargin;
                return;
            }
        }
        catch
        {
            // fall through to the primary-monitor fallback
        }

        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + TopMargin;
    }

    /// <summary>Re-raises the overlay to the top of the topmost band so a toast is visible above the game.</summary>
    private void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    void IAchievementOverlay.ShowToast(string title, string subtitle, string? iconPath) =>
        ShowToast(title, subtitle, iconPath);

    public void Dispose()
    {
        _hideTimer.Stop();
        Close();
    }

    // ---- Win32 interop (x64; this app publishes win-x64 only) ----

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private const int MONITOR_DEFAULTTONEAREST = 2;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
