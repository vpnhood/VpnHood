using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// What a host without a service says for itself (InProcessDesktopHost): where the window keeps things,
// how this OS tells who owns the far end of a loopback connection, the process's log, and the app,
// which runs in the window's process. A platform package implements it (Desktop.WindowsVpnPlugin).
public interface IInProcessDesktopPlatform
{
    IDesktopUiPaths Paths { get; }
    ILoopbackPeerCheck PeerCheck { get; }

    // The desktop session this launch runs in, as the window's single instance asks it.
    IDesktopSession DesktopSession { get; }

    // Whether the window may keep a tray icon (DesktopUiParams.UseTray).
    bool UseTray { get; }

    // The OS's ways for links and the clipboard where the UI's own are refused (DesktopUiParams.Shell); null: the
    // UI's own.
    IDesktopShell? Shell { get; }

    // The process's log, added once this launch holds the window: a second launch, which only brings
    // that window forward, leaves its file alone.
    ILoggerProvider CreateLoggerProvider();

    // The app, built once this launch holds the window, so a second launch builds none. Disposing it
    // ends the app.
    IAppDaemonHost CreateApp();
}
