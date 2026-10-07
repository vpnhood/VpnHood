using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop;

// What the platform says for itself: the four answers every command reaches for that are not the
// app's, and the ones only some platforms have. A platform package (Desktop.Linux, Desktop.Windows)
// implements it and hands it to DesktopHost with the head's DesktopInitParams; nothing in this
// project reads a static.
public interface IDesktopPlatform
{
    IAppDesktopPaths Paths { get; }
    IAppInstanceController Instance { get; }

    // Where the service hands its API's address to an administrator.
    IDaemonChannel Channel { get; }

    // How this OS tells who owns the far end of a loopback connection, checked before a call is sent.
    ILoopbackPeerCheck PeerCheck { get; }

    // Registering and removing the instance, where the app does it itself (Windows: the service, which
    // "service install" registers); null where the package's installer does (Linux), and then those
    // commands are not offered.
    IAppInstanceSetup? InstanceSetup { get; }

    // The app for "daemon", which the systemd unit or the service control manager runs, and for "dev".
    IAppDaemonHostFactory DaemonHostFactory { get; }

    // Whether a tray keeps the UI (Windows): closing the window then only hides it. Where there is
    // none (Linux), closing the window ends the UI. The service runs on either way.
    bool IsTraySupported { get; }

    // Whether whoever runs this process may use the app: an administrator, by the rule the service
    // applies to every caller of its channel. The window and the commands ask it first, so a standard
    // user is told at once, with no prompt and no wait; the service's own check stays the boundary.
    bool IsAdministrator();

    // The tray, started beside the window over the same API; null where the platform has none.
    IDisposable? CreateTray(DesktopTrayParams trayParams);

    // The console as this platform has it, for the process that holds the app ("daemon", "dev"),
    // which adds it to the log before anything logs: the journal's sink where stdout is the journal
    // (Linux under systemd), a terminal's otherwise.
    ILoggerProvider CreateConsoleLoggerProvider();

    // The platform's own log, for the window, which runs as the person and has no console: its warnings
    // and errors go where "service log" looks when the service gives no log - the Application log
    // under the service's name on Windows, the journal under the instance's name on Linux.
    ILoggerProvider CreateSystemLogLoggerProvider();

    // The daemon's run as the platform's service manager hosts it. Windows' stops it with a call
    // rather than a signal (the service control manager, through ServiceBase), which then cancels the
    // run; where a signal stops it (Linux), the run is simply run, and the parser turns the signal
    // into the same cancellation.
    Task<int> HostDaemon(Func<CancellationToken, Task<int>> run, CancellationToken cancellationToken);
}
