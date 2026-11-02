namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// The connection's state as the desktop shows it outside the window - on the tray's icon and the taskbar button:
// connected, on its way (connecting, or unstable), or idle.
public enum DesktopConnectionBadge
{
    Disconnected,
    Connecting,
    Connected
}
