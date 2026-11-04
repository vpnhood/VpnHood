using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.AvaloniaUI.Dev;

// The UI whose window keeps away from the keyboard and the eye ("--no-activate"), for a UI walk run while a person
// types in another window. It opens without activation and under every other window: every show or move of it is told
// so, as Avalonia shows a window that is visible already with SW_RESTORE, which activates; and Windows activates it on
// no click
// (WS_EX_NOACTIVATE, added each time Avalonia sets the window's styles, with WS_EX_APPWINDOW for the taskbar button
// such a window loses otherwise). UI Automation's Invoke, which drives the walk, activates it all the same, from
// outside the app: the foreground is handed back at once to the window that had it, which Windows allows the
// foreground process. A thread of its own watches the foreground, so a UI thread busy drawing a page cannot hold the
// hand-back.
internal sealed class NoActivateAvaloniaApp : ClassicAvaloniaApp
{
    private const uint WsExAppWindow = 0x00040000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmWindowPosChanging = 0x0046;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WmHandBack = 0x8001; // WM_APP + 1, on the watch thread's own queue
    private static readonly nint HwndBottom = 1;

    // WINDOWPOS: the window and the one it goes after, then x, y, cx and cy, then the flags
    private static readonly int WindowPosInsertAfterOffset = IntPtr.Size;
    private static readonly int WindowPosFlagsOffset = 2 * IntPtr.Size + 4 * sizeof(int);
    private static readonly uint ProcessId = (uint)Environment.ProcessId;

    // kept for the hook's life: Windows calls it as long as the process runs
    private static readonly WinEventProc ForegroundChanged = OnForegroundChanged;
    private static uint _watchThreadId;
    private static nint _lastForeignForeground;

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            return;

        window.ShowActivated = false;
        if (!OperatingSystem.IsWindows())
            return;

        Win32Properties.AddWindowStylesCallback(window,
            (style, exStyle) => (style, exStyle | WsExNoActivate | WsExAppWindow));
        Win32Properties.AddWndProcHookCallback(window, OnWindowMessage);
        WatchForeground();
    }

    // every show or move: without activation, and under every other window, where a person does not see it and it
    // still draws, for the walk's pictures; a minimized window would not
    private static nint OnWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging) {
            var flags = Marshal.ReadInt32(lParam, WindowPosFlagsOffset);
            if ((flags & SwpNoZOrder) == 0)
                Marshal.WriteIntPtr(lParam, WindowPosInsertAfterOffset, HwndBottom);
            Marshal.WriteInt32(lParam, WindowPosFlagsOffset, flags | SwpNoActivate);
        }

        return IntPtr.Zero;
    }

    // The watch thread: the hook, whose events its message loop delivers, and the hand-back, posted to the same loop
    // so it comes after the switch that raised it - one asked for within the switch is refused. A watch that cannot
    // start fails the start: the window would take the keyboard.
    private static void WatchForeground()
    {
        using var started = new ManualResetEventSlim();
        var isWatching = false;
        var thread = new Thread(() => {
            _watchThreadId = GetCurrentThreadId();
            _lastForeignForeground = GetForegroundWindow();
            isWatching = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, ForegroundChanged,
                0, 0, WinEventOutOfContext) != IntPtr.Zero;
            // ReSharper disable once AccessToDisposedClosure
            started.Set();
            if (!isWatching)
                return;

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) {
                if (message.Message == WmHandBack && !HandBack())
                    VhLogger.Instance.LogWarning("Could not hand the foreground back, so the window keeps the keyboard.");
            }
        }) { IsBackground = true, Name = "Foreground watch" };
        thread.Start();
        started.Wait();
        if (!isWatching)
            throw new InvalidOperationException("Could not watch the foreground, so the window could take the keyboard.");
    }

    // Windows takes a foreground change only from a thread of the foreground input, which this one joins for the call:
    // owning the foreground is not enough for a thread of its own
    private static bool HandBack()
    {
        var foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var isAttached = foregroundThreadId != _watchThreadId &&
                         AttachThreadInput(_watchThreadId, foregroundThreadId, true);
        try {
            return SetForegroundWindow(_lastForeignForeground);
        }
        finally {
            if (isAttached)
                AttachThreadInput(_watchThreadId, foregroundThreadId, false);
        }
    }

    private static void OnForegroundChanged(nint hook, uint eventType, nint hWnd, int idObject, int idChild,
        uint eventThread, uint eventTime)
    {
        GetWindowThreadProcessId(hWnd, out var processId);
        if (processId != ProcessId)
            _lastForeignForeground = hWnd;
        else if (_lastForeignForeground != IntPtr.Zero)
            PostThreadMessage(_watchThreadId, WmHandBack, IntPtr.Zero, IntPtr.Zero);
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hWnd, int idObject, int idChild,
        uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
