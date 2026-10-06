using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// A WPF window at the size and place DesktopWindowFit gives the primary screen's work area. A WPF
// window's size counts its frame, which is measured off the window's handle, so the window's style
// is set first.
internal static class WpfWindowFit
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, [Out] int[] rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, [Out] int[] rect);

    public static void Apply(Window window, VhSize phoneSize, bool isTv)
    {
        var frame = FrameOf(window);
        var workArea = SystemParameters.WorkArea;
        var placement = DesktopWindowFit.Fit(new VhRect(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            frame.Width, frame.Height, phoneSize, isTv);
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = placement.ContentWidth + frame.Width;
        window.Height = placement.ContentHeight + frame.Height;
    }

    // the window's rectangle less its client area's, in DIPs
    private static Size FrameOf(Window window)
    {
        var hWnd = new WindowInteropHelper(window).EnsureHandle();
        var windowRect = new int[4];
        var clientRect = new int[4];
        if (!GetWindowRect(hWnd, windowRect) || !GetClientRect(hWnd, clientRect))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var dpi = VisualTreeHelper.GetDpi(window);
        return new Size(
            (windowRect[2] - windowRect[0] - clientRect[2]) / dpi.DpiScaleX,
            (windowRect[3] - windowRect[1] - clientRect[3]) / dpi.DpiScaleY);
    }
}
