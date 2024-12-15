using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace WinNative;

public class SystemTray : IDisposable
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired,
        uint fuLoad);

    public event EventHandler? DoubleClicked;
    public event EventHandler? Clicked;
    public ContextMenu? ContextMenu { get; set; }

    private readonly Window _wnd;
    private const uint Message = 0x0500; //WM_USER = 0x0400;
    private const uint SystemTrayId = 100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    private struct NotifyIconData
    {
        public int cbSize; // = Marshal.SizeOf(typeof(NotifyIconData))
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x80)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x100)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x40)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private enum NotifyIconMessage
    {
        NimAdd = 0x00000000,
        NimModify = 0x00000001,
        NimDelete = 0x00000002
    }

    private enum ContextMenuEvent
    {
        Contextmenu = 0x7B,
        RightButtonUp = 0x205,
        LeftButtonUp = 0x202,
        LeftButtonDblClick = 0x203
    }

    [Flags]
    private enum NotifyIconFlag
    {
        Message = 0x01,
        Icon = 0x02,
        Tip = 0x04
    }


    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Message && (int)wParam == SystemTrayId) {
            switch ((int)lParam) {
                case (int)ContextMenuEvent.LeftButtonUp:
                    Clicked?.Invoke(this, EventArgs.Empty);
                    break;

                case (int)ContextMenuEvent.LeftButtonDblClick:
                    DoubleClicked?.Invoke(this, EventArgs.Empty);
                    break;

                case (int)ContextMenuEvent.RightButtonUp:
                case (int)ContextMenuEvent.Contextmenu:
                    ContextMenu?.Show();
                    break;
            }
        }

        return Window.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private NotifyIconData _notificationData;
    private string _tip;

    public SystemTray(string tip, nint hIcon)
    {
        _tip = tip;
        _wnd = new Window(WndProc);
        _notificationData = new NotifyIconData {
            cbSize = Marshal.SizeOf(typeof(NotifyIconData)),
            hWnd = _wnd.Handle,
            uID = SystemTrayId,
            uCallbackMessage = Message,
            uFlags = (uint)(NotifyIconFlag.Message | NotifyIconFlag.Icon | NotifyIconFlag.Tip),
            hIcon = hIcon,
            szTip = tip
        };

        Shell_NotifyIcon((uint)NotifyIconMessage.NimAdd, ref _notificationData);
    }

    public void Update(string text, IntPtr hIcon)
    {
        _notificationData.hIcon = hIcon;
        _notificationData.szTip = text;
        Shell_NotifyIcon((uint)NotifyIconMessage.NimModify, ref _notificationData);
    }

    public string Tip {
        get => _tip;
        set {
            _tip = value;
            _notificationData.szTip = value;
            Shell_NotifyIcon((uint)NotifyIconMessage.NimModify, ref _notificationData);
        }
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue) return;

        if (disposing)
            _wnd.Dispose();

        // free unmanaged resources (unmanaged objects) and override a finalizer below.
        // set large fields to null.
        _disposedValue = true;
        Shell_NotifyIcon((uint)NotifyIconMessage.NimDelete, ref _notificationData);
    }

    ~SystemTray()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(false);
    }

    // This code added to correctly implement the disposable pattern.
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
        // uncomment the following line if the finalizer is overridden above.
        GC.SuppressFinalize(this);
    }
}