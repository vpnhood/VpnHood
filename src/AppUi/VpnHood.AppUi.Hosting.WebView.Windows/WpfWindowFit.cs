using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// A WPF window at the size and place DesktopWindowFit gives the primary screen's work area, and again
// whenever that changes, as a new resolution or a remote session's resized window changes it. A WPF
// window's size counts its frame, which is measured off the window's handle, so the window's style
// is set first.
internal static class WpfWindowFit
{
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, [Out] int[] rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, [Out] int[] rect);

    public static void Apply(Window window, VhSize phoneSize, bool isTv)
    {
        // measured once, before the window opens: a minimized window's rectangle is its button's
        var hWnd = new WindowInteropHelper(window).EnsureHandle();
        var frame = FrameOf(window, hWnd);
        Fit(window, frame, phoneSize, isTv);

        // The taskbar takes its new place after the resolution's change, and says so with the work
        // area's; WPF's SystemParameters reads it from the same message, so the refit comes after.
        var source = HwndSource.FromHwnd(hWnd) ?? throw new InvalidOperationException("The window has no HwndSource.");
        source.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr _, ref bool _) => {
            if (msg == WmDisplayChange || (msg == WmSettingChange && wParam == SpiSetWorkArea))
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Fit(window, frame, phoneSize, isTv));
            return IntPtr.Zero;
        });
    }

    private static void Fit(Window window, VhSize frame, VhSize phoneSize, bool isTv)
    {
        var workArea = SystemParameters.WorkArea;
        var placement = DesktopWindowFit.Fit(new VhRect(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            frame, phoneSize, isTv);
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = placement.ContentSize.Width + frame.Width;
        window.Height = placement.ContentSize.Height + frame.Height;
    }

    // the window's rectangle less its client area's, in DIPs
    private static VhSize FrameOf(Window window, IntPtr hWnd)
    {
        var windowRect = new int[4];
        var clientRect = new int[4];
        if (!GetWindowRect(hWnd, windowRect) || !GetClientRect(hWnd, clientRect))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var dpi = VisualTreeHelper.GetDpi(window);
        return new VhSize(
            (windowRect[2] - windowRect[0] - clientRect[2]) / dpi.DpiScaleX,
            (windowRect[3] - windowRect[1] - clientRect[3]) / dpi.DpiScaleY);
    }
}
