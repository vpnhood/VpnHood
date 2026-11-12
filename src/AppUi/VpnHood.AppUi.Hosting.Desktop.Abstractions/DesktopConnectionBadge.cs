namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Specifies the connection's state as the desktop shows it outside the window, on the tray's icon and the taskbar button.</summary>
public enum DesktopConnectionBadge
{
    /// <summary>The app is idle and reports no kill switch block.</summary>
    Disconnected,

    /// <summary>The connection is on its way or changing: connecting, unstable, disconnecting or updating.</summary>
    Connecting,

    /// <summary>The app is connected.</summary>
    Connected,

    /// <summary>The app is not connected, and the kill switch blocks the internet, but for the exclusions it keeps.</summary>
    Blocked
}
