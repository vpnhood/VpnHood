using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

// ReSharper disable IdentifierTypo
namespace VpnHood.AppLib.App.Windows.WinNative;

// A thread of its own with the Win32 message loop that windows made on it need - a tray icon's
// hidden windows, its menu - so they answer without asking anything of the UI's thread, whatever
// UI runs there. Work is posted to it and runs in order, on it; stopping it runs what was posted
// first. Posted through a window of its own rather than to the thread, since a thread's messages are
// dropped while a menu is open and a window's are not.
public sealed class MessageLoopThread : IDisposable
{
    private const uint WmRun = 0x8001; // WM_APP + 1
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private Window? _window;
    private bool _disposed;

    public MessageLoopThread(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
    }

    private void Run()
    {
        _window = new Window(WndProc);
        _started.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        RunPosted();
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmRun)
            return Window.DefWindowProc(hWnd, msg, wParam, lParam);

        RunPosted();
        return IntPtr.Zero;
    }

    private void RunPosted()
    {
        while (_actions.TryDequeue(out var action)) {
            try {
                action();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "A task on the {ThreadName} thread failed.", _thread.Name);
            }
        }
    }

    // From any thread.
    public void Post(Action action)
    {
        var window = _window ?? throw new InvalidOperationException("The message loop has not started.");
        _actions.Enqueue(action);
        PostMessage(window.Handle, WmRun, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Post(() => PostQuitMessage(0));
        _thread.Join(TimeSpan.FromSeconds(5));
        _started.Dispose();
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

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);
}
