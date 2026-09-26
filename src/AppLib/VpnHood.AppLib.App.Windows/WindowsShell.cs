using System.Diagnostics;
using System.Runtime.InteropServices;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppLib.App.Windows;

// What a Windows UI asks of the shell and the window manager, whichever UI it is: a link in the
// person's browser, a title bar in the app's colour.
public static class WindowsShell
{
    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);

    // The person's default browser, as a click on a link would open it. Run as the person, so only a
    // process of theirs - the window, never the service - may call it.
    public static void OpenUrl(Uri url)
    {
        Process.Start(new ProcessStartInfo {
            FileName = url.AbsoluteUri,
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public static void SetTitleBarColor(IntPtr hWnd, VhColor color)
    {
        var attrValue = new[] { (color.B << 16) | (color.G << 8) | color.R };
        const int captionColor = 35;
        DwmSetWindowAttribute(hWnd, captionColor, attrValue, attrValue.Length * 4);
    }
}
