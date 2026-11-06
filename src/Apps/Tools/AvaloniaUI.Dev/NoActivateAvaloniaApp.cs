using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.AvaloniaUI.Dev;

// The UI whose window keeps away from the keyboard ("--no-activate"), for a UI walk run while a person types in another
// window. It opens without activation: every show or move of it is told so, as Avalonia shows a window that is visible
// already with SW_RESTORE, which activates; and Windows activates it on no click (WS_EX_NOACTIVATE, added each time
// Avalonia sets the window's styles, with WS_EX_APPWINDOW for the taskbar button such a window loses otherwise). It stays
// on top, to be watched over the window they type in, and "--minimized" opens it out of their way. A walk may park it
// off the screen, where it still draws for its pictures: its taskbar button brings it back, and it then stays in view.
// A walk presses through WM_COPYDATA, which names the control. Keep it so: UI Automation's Invoke takes the foreground
// from outside the app and handing it back does not hold, and a posted click misses in a window minimized after it was
// shown.
internal sealed class NoActivateAvaloniaApp : ClassicAvaloniaApp
{
    private const uint WsExAppWindow = 0x00040000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmActivate = 0x0006;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint WmCopyData = 0x004A;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoActivate = 0x0010;
    private const int MinimizedPosition = -32000;

    // WINDOWPOS: the window and the one it goes after, then x, y, cx and cy, then the flags
    private static readonly int WindowPosXOffset = 2 * IntPtr.Size;
    private static readonly int WindowPosFlagsOffset = WindowPosXOffset + 4 * sizeof(int);

    private Window? _window;
    private bool _isWatched;

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            return;

        window.ShowActivated = false;
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
            window.WindowState = WindowState.Minimized;

        if (!OperatingSystem.IsWindows())
            return;

        _window = window;
        window.Topmost = true;
        Win32Properties.AddWindowStylesCallback(window,
            (style, exStyle) => (style, exStyle | WsExNoActivate | WsExAppWindow));
        Win32Properties.AddWndProcHookCallback(window, OnWindowMessage);
    }

    private nint OnWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging) {
            var flags = Marshal.ReadInt32(lParam, WindowPosFlagsOffset) | SwpNoActivate;
            if (_isWatched && IsMoveOffScreen(lParam, flags))
                flags |= SwpNoMove;
            Marshal.WriteInt32(lParam, WindowPosFlagsOffset, flags);
        }

        // Only a person activates this window, by its taskbar button: a walk's presses and posted keys never do. A walk
        // that activates it would pull it onto their screen.
        if (msg == WmActivate && (wParam & 0xFFFF) != 0) {
            _isWatched = true;
            Dispatcher.UIThread.Post(BringOnScreen);
        }

        if (msg != WmCopyData)
            return IntPtr.Zero;

        // Avalonia drops what a hook returns, so the walk's answer goes by ReplyMessage
        handled = true;
        var data = Marshal.PtrToStructure<CopyData>(lParam);
        ReplyMessage(Press(Marshal.PtrToStringUni(data.Text, data.Size / sizeof(char))) ? 1 : 0);
        return IntPtr.Zero;
    }

    // Windows parks a minimized window at MinimizedPosition, which is no one's move to refuse.
    private bool IsMoveOffScreen(nint windowPos, int flags)
    {
        var x = Marshal.ReadInt32(windowPos, WindowPosXOffset);
        if ((flags & SwpNoMove) != 0 || x == MinimizedPosition || _window is not { } window)
            return false;

        var y = Marshal.ReadInt32(windowPos, WindowPosXOffset + sizeof(int));
        var size = (flags & SwpNoSize) != 0
            ? PixelSize.FromSize(window.ClientSize, window.RenderScaling)
            : new PixelSize(Marshal.ReadInt32(windowPos, WindowPosXOffset + 2 * sizeof(int)),
                Marshal.ReadInt32(windowPos, WindowPosXOffset + 3 * sizeof(int)));

        return !IsOnAScreen(window, new PixelRect(new PixelPoint(x, y), size));
    }

    private void BringOnScreen()
    {
        if (_window is not { WindowState: not WindowState.Minimized } window || window.Screens.Primary is not { } screen)
            return;

        var size = PixelSize.FromSize(window.ClientSize, window.RenderScaling);
        if (IsOnAScreen(window, new PixelRect(window.Position, size)))
            return;

        var area = screen.WorkingArea;
        window.Position = new PixelPoint(area.X + Math.Max(0, (area.Width - size.Width) / 2),
            area.Y + Math.Max(0, (area.Height - size.Height) / 2));
    }

    private static bool IsOnAScreen(Window window, PixelRect rect)
    {
        return window.Screens.All.Any(x => x.Bounds.Intersects(rect));
    }

    // A card carries its name on a container whose Button is a CHILD, while a label's button is an ANCESTOR; a finger
    // resolves both without thinking, so a press does too. Only Invoke is honoured, never Toggle: a walk reads the app,
    // it does not change its settings.
    private bool Press(string name)
    {
        var control = _window?.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(x => x.Name == name && x.IsEffectivelyVisible && x.Bounds is { Width: >= 1, Height: >= 1 });

        return control != null && (TryInvoke(control) ||
                                   control.GetVisualDescendants().OfType<Control>().Any(TryInvoke) ||
                                   control.GetVisualAncestors().OfType<Control>().Any(TryInvoke));
    }

    private static bool TryInvoke(Control control)
    {
        if (ControlAutomationPeer.CreatePeerForElement(control).GetProvider<IInvokeProvider>() is not { } provider)
            return false;

        try {
            provider.Invoke();
            return true;
        }
        catch (ElementNotEnabledException) {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public nint Kind;
        public int Size;
        public nint Text;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReplyMessage(nint result);
}
