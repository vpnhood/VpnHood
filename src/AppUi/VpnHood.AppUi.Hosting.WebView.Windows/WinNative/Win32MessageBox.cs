using System.Runtime.InteropServices;
using VpnHood.AppUi.Hosting.Abstractions;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// The one message shown in the UI's place (IDesktopUi.RunMessage): Windows' own message box, the app's
// name its title and OK its one button, on the calling thread until the person closes it. Another
// thread finds it among the windows of the thread showing it: to close it when the host cancels, and
// to bring it forward for a second launch.
internal static class Win32MessageBox
{
    private const uint MbOk = 0x0000;
    private const uint MbIconError = 0x0010;
    private const uint MbIconWarning = 0x0030;
    private const uint MbSetForeground = 0x00010000;
    private const uint WmClose = 0x0010;
    private const string DialogClassName = "#32770";
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static uint CurrentThreadId => GetCurrentThreadId();

    // crisp at any scale, as this UI's windows are (Win32Window)
    public static void Show(string title, string text, DesktopUiMessageKind kind)
    {
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        var icon = kind == DesktopUiMessageKind.Failure ? MbIconError : MbIconWarning;
        MessageBoxW(IntPtr.Zero, text, title, MbOk | icon | MbSetForeground);
    }

    // From any thread.
    public static void Close(uint threadId)
    {
        ForEachBox(threadId, hWnd => PostMessageW(hWnd, WmClose, IntPtr.Zero, IntPtr.Zero));
    }

    // From any thread.
    public static void BringToFront(uint threadId)
    {
        ForEachBox(threadId, hWnd => SetForegroundWindow(hWnd));
    }

    private static void ForEachBox(uint threadId, Action<IntPtr> action)
    {
        EnumThreadWindows(threadId, (hWnd, _) => {
            var className = new char[DialogClassName.Length + 1];
            var length = GetClassNameW(hWnd, className, className.Length);
            if (new string(className, 0, length) == DialogClassName)
                action(hWnd);
            return true;
        }, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
