using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Windows.WinNative;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// A top-level window of this UI, run by the message loop of the thread that made it (RunMessageLoop),
// which also runs the work posted to it from any thread. The windows share one class; a message
// reaches its window through the handle, and what the window's owner does not answer goes to
// Windows' default. Every window wears the executable's icon.
internal sealed class Win32Window
{
    // what the owner answers a message with; null leaves it to Windows' default
    public delegate IntPtr? MessageHandler(uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const string ClassName = "VpnHood.AppUi.Hosting.WebView.Windows";
    private const int CwUseDefault = unchecked((int)0x80000000);
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmSetIcon = 0x0080;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmRun = 0x8001; // WM_APP + 1
    private const int SwHide = 0;
    private const int SwShowNormal = 1;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr IdcArrow = new(32512);
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    // kept alive: Windows calls it for as long as the class is registered
    private static readonly WndProc ClassWndProc = OnWindowMessage;
    private static readonly Lazy<(IntPtr Large, IntPtr Small)> ExeIcons = new(LoadExeIcons);
    private static readonly Dictionary<IntPtr, Win32Window> Windows = [];
    private static bool _isClassRegistered;

    // the window being made: the messages it gets before it has a handle find it here
    [ThreadStatic] private static Win32Window? _creating;

    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly MessageHandler _onMessage;
    private readonly IntPtr _backgroundBrush;
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public IntPtr Handle { get; }

    // Hidden, at Windows' default place and size, until its owner sizes and shows it.
    public Win32Window(string title, uint style, IntPtr owner, VhColor? background, MessageHandler onMessage)
    {
        _onMessage = onMessage;
        _backgroundBrush = background is { } color
            ? CreateSolidBrush((uint)(color.R | (color.G << 8) | (color.B << 16)))
            : IntPtr.Zero;

        // Windows' scaling of the thread's windows, on every screen: crisp at any scale, whatever the
        // head's manifest says. The thread keeps it.
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);

        var hInstance = GetModuleHandleW(null);
        RegisterClass(hInstance);
        _creating = this;
        try {
            Handle = CreateWindowExW(0, ClassName, title, style, CwUseDefault, CwUseDefault, CwUseDefault,
                CwUseDefault, owner, IntPtr.Zero, hInstance, IntPtr.Zero);
        }
        finally {
            _creating = null;
        }

        if (Handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        SendMessageW(Handle, WmSetIcon, new IntPtr(1), ExeIcons.Value.Large);
        SendMessageW(Handle, WmSetIcon, IntPtr.Zero, ExeIcons.Value.Small);
    }

    public bool IsOwnThread => Environment.CurrentManagedThreadId == _threadId;
    public bool IsMinimized => IsIconic(Handle);
    public bool IsVisible => IsWindowVisible(Handle);

    // its screen's DPI over Windows' 96
    public double Scale => GetDpiForWindow(Handle) / 96.0;

    public Win32Rect WindowRect => GetWindowRect(Handle, out var rect) ? rect : throw new Win32Exception(Marshal.GetLastWin32Error());
    public Win32Rect ClientRect => GetClientRect(Handle, out var rect) ? rect : throw new Win32Exception(Marshal.GetLastWin32Error());

    public void Show() => ShowWindow(Handle, SwShowNormal);
    public void Hide() => ShowWindow(Handle, SwHide);
    public void Destroy() => DestroyWindow(Handle);

    // shown, restored where it was minimized, and in front
    public void BringToFront()
    {
        ShowWindow(Handle, IsIconic(Handle) ? SwRestore : SwShow);
        SetForegroundWindow(Handle);
    }

    // in physical pixels, the frame included
    public void SetBounds(int x, int y, int width, int height)
    {
        SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
    }

    public void SetSize(int width, int height)
    {
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate);
    }

    // From any thread; dropped once the window is gone.
    public void Post(Action action)
    {
        _actions.Enqueue(action);
        PostMessageW(Handle, WmRun, IntPtr.Zero, IntPtr.Zero);
    }

    // the thread's messages, until a window of it quits the loop (Quit)
    public static void RunMessageLoop()
    {
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0) {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public static void Quit()
    {
        PostQuitMessage(0);
    }

    private static IntPtr OnWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!Windows.TryGetValue(hWnd, out var window)) {
            if (_creating == null)
                return DefWindowProcW(hWnd, msg, wParam, lParam);

            window = _creating;
            Windows[hWnd] = window;
        }

        try {
            if (window.OnMessage(hWnd, msg, wParam, lParam) is { } result)
                return result;
        }
        catch (Exception ex) {
            // thrown on, it would cross Windows' own frames and end the process
            VhLogger.Instance.LogError(ex, "A window could not handle a message. Message: {Message}", msg);
        }
        finally {
            if (msg == WmNcDestroy) {
                Windows.Remove(hWnd);
                if (window._backgroundBrush != IntPtr.Zero)
                    DeleteObject(window._backgroundBrush);
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private IntPtr? OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg) {
            case WmRun:
                RunPosted();
                return IntPtr.Zero;

            case WmEraseBkgnd when _backgroundBrush != IntPtr.Zero: {
                GetClientRect(hWnd, out var clientRect);
                FillRect(wParam, ref clientRect, _backgroundBrush);
                return new IntPtr(1);
            }

            // on a screen of another scale, the place and size Windows gives it there
            case WmDpiChanged: {
                var suggested = Marshal.PtrToStructure<Win32Rect>(lParam);
                SetWindowPos(hWnd, IntPtr.Zero, suggested.Left, suggested.Top, suggested.Width, suggested.Height,
                    SwpNoZOrder | SwpNoActivate);
                return IntPtr.Zero;
            }
        }

        return _onMessage(msg, wParam, lParam);
    }

    private void RunPosted()
    {
        while (_actions.TryDequeue(out var action)) {
            try {
                action();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "A task posted to the window failed.");
            }
        }
    }

    private static void RegisterClass(IntPtr hInstance)
    {
        if (_isClassRegistered)
            return;

        var windowClass = new WndClassExW {
            cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ClassWndProc),
            hInstance = hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, IdcArrow),
            lpszClassName = ClassName
        };

        if (RegisterClassExW(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        _isClassRegistered = true;
    }

    private static (IntPtr Large, IntPtr Small) LoadExeIcons()
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        return Environment.ProcessPath is { } path && WinIcon.ExtractIconEx(path, 0, large, small, 1) > 0
            ? (large[0], small[0])
            : (IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Win32Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Win32Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
