namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// The connection's state as the desktop shows it outside the window - on the tray's icon and the taskbar button:
// connected, on its way (connecting, or unstable), idle, or idle with the kill switch blocking the internet
// (AppState.IsKillSwitchBlocking).
public enum DesktopConnectionBadge
{
    Disconnected,
    Connecting,
    Connected,
    Blocked
}
