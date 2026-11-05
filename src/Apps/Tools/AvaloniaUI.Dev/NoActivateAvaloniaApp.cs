using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using VpnHood.AppUi.Presentation.Classic.Avalonia;

namespace VpnHood.App.AvaloniaUI.Dev;

// The UI whose window keeps away from the keyboard ("--no-activate"), for a UI walk run while a person types in another
// window. It opens without activation: every show or move of it is told so, as Avalonia shows a window that is visible
// already with SW_RESTORE, which activates; and Windows activates it on no click (WS_EX_NOACTIVATE, added each time
// Avalonia sets the window's styles, with WS_EX_APPWINDOW for the taskbar button such a window loses otherwise). It stays
// on top, to be watched over the window they type in, and "--minimized" opens it out of their way.
// A walk presses through WM_COPYDATA, which names the control. Keep it so: UI Automation's Invoke takes the foreground
// from outside the app and handing it back does not hold, and a posted click misses in a window minimized after it was
// shown.
internal sealed class NoActivateAvaloniaApp : ClassicAvaloniaApp
{
    private const uint WsExAppWindow = 0x00040000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint WmCopyData = 0x004A;
    private const int SwpNoActivate = 0x0010;

    // WINDOWPOS: the window and the one it goes after, then x, y, cx and cy, then the flags
    private static readonly int WindowPosFlagsOffset = 2 * IntPtr.Size + 4 * sizeof(int);

    private Window? _window;

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
        if (msg == WmWindowPosChanging)
            Marshal.WriteInt32(lParam, WindowPosFlagsOffset, Marshal.ReadInt32(lParam, WindowPosFlagsOffset) | SwpNoActivate);

        if (msg != WmCopyData)
            return IntPtr.Zero;

        // Avalonia drops what a hook returns, so the walk's answer goes by ReplyMessage
        handled = true;
        var data = Marshal.PtrToStructure<CopyData>(lParam);
        ReplyMessage(Press(Marshal.PtrToStringUni(data.Text, data.Size / sizeof(char))) ? 1 : 0);
        return IntPtr.Zero;
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
