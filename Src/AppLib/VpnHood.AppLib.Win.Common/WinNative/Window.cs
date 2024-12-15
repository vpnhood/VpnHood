using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace WinNative;

public class Window : IDisposable
{
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    [SuppressMessage("ReSharper", "IdentifierTypo")]
    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        [MarshalAs(UnmanagedType.U4)] public uint cbSize;
        [MarshalAs(UnmanagedType.U4)] public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.U2)]
    private static extern short RegisterClassEx([In] ref WndClassEx wndClassEx);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, [MarshalAs(UnmanagedType.LPStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly WndClassEx _windowClass;

    public IntPtr Handle { get; }

    public Window(WndProc wndProc)
    {
        var hInstance = Process.GetCurrentProcess().Handle;
        var className = Guid.NewGuid().ToString();

        //must keep reference to it and prevent it from release. Otherwise, exception will occur in message pump
        _windowClass = new WndClassEx {
            lpfnWndProc = wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            style = 0,
            cbSize = (uint)Marshal.SizeOf(typeof(WndClassEx))
        };

        RegisterClassEx(ref _windowClass);
        Handle = CreateWindowEx(
            0,
            className,
            "", // window caption
            0, // window style
            -1, // initial x position
            -1, // initial y position
            -1, // initial x size
            -1, // initial y size
            IntPtr.Zero, // parent window handle
            IntPtr.Zero, // window menu handle
            hInstance, // program instance handle
            IntPtr.Zero); // creation parameters
    }

    private bool _disposedValue; // To detect redundant calls

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        if (disposing) {
            // dispose managed state (managed objects).
        }

        // free unmanaged resources (unmanaged objects) and override a finalizer below.
        // set large fields to null.
        UnregisterClass(_windowClass.lpszClassName, _windowClass.hInstance);
        DestroyWindow(Handle);
        _disposedValue = true;
    }

    ~Window()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}