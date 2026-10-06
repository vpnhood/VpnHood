using System.ComponentModel;
using System.Runtime.InteropServices;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// A window at the size and place DesktopWindowFit gives the work area of the screen it is on, in that
// screen's logical units: its pixels over its scale. The frame is the window's rectangle less its
// client area's, so the window has its style first; a minimized window's rectangle is its button's,
// so it is not fitted then.
internal static class Win32WindowFit
{
    private const uint MonitorDefaultToPrimary = 1;

    public static void Apply(Win32Window window, VhSize phoneSize, bool isTv)
    {
        var monitorInfo = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(window.Handle, MonitorDefaultToPrimary), ref monitorInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var scale = window.Scale;
        var windowRect = window.WindowRect;
        var clientRect = window.ClientRect;
        var frameWidth = windowRect.Width - clientRect.Width;
        var frameHeight = windowRect.Height - clientRect.Height;
        var workArea = monitorInfo.rcWork;
        var placement = DesktopWindowFit.Fit(
            new VhRect(workArea.Left / scale, workArea.Top / scale, workArea.Width / scale, workArea.Height / scale),
            new VhSize(frameWidth / scale, frameHeight / scale), phoneSize, isTv);

        window.SetBounds(
            (int)Math.Round(placement.Left * scale),
            (int)Math.Round(placement.Top * scale),
            (int)Math.Round(placement.ContentSize.Width * scale) + frameWidth,
            (int)Math.Round(placement.ContentSize.Height * scale) + frameHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Win32Rect rcMonitor;
        public Win32Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
