using System.Runtime.InteropServices;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// Win32's RECT, in physical pixels.
[StructLayout(LayoutKind.Sequential)]
internal struct Win32Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}
