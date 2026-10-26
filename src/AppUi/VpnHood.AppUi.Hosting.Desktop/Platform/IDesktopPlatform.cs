using Microsoft.Extensions.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// What the platform says for itself: the four answers every command reaches for that are not the
// app's, and the ones only some platforms have. A platform package (Desktop.Linux, Desktop.WindowsTun)
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

    // The desktop session this launch runs in, as the window's single instance asks it.
    IDesktopSession DesktopSession { get; }

    // Registering and removing the instance, where the app does it itself (Windows: the service, which
    // "service install" registers); null where the package's installer does (Linux), and then those
    // commands are not offered.
    IAppInstanceSetup? InstanceSetup { get; }

    // The app for "daemon", which the systemd unit or the service control manager runs, and for "dev".
    IAppDaemonHostFactory DaemonHostFactory { get; }

    // Whether whoever runs this process may use the app: an administrator, by the rule the service
    // applies to every caller of its channel. The window and the commands ask it first, so a standard
    // user is told at once, with no prompt and no wait; the service's own check stays the boundary.
    bool IsAdministrator();

    // The console as this platform has it, for the process that holds the app ("daemon", "dev"),
    // which adds it to the log before anything logs: the journal's sink where stdout is the journal
    // (Linux under systemd), a terminal's otherwise.
    ILoggerProvider CreateConsoleLoggerProvider();

    // The platform's own log, for the window, which runs as the person and has no console: its warnings
    // and errors go where "service log" looks when the service gives no log - the Application log
    // under the service's name on Windows, the journal under the instance's name on Linux.
    ILoggerProvider CreateSystemLogLoggerProvider();

    // The window started again, as it was started - in the tray, or open - by the install's own launcher, which
    // runs the version the install names now: an update replaced the service under the open window. A process of
    // its own, which outlives this one; nothing for a copy run where it lies, which has no such launcher.
    void StartUiAgain(bool startHidden);

    // The daemon's run as the platform's service manager hosts it, with the time the stop leaves the
    // app's graceful close (DaemonRun), so the adapter is released before the manager ends the process.
    // Windows' stops it with a call rather than a signal (the service control manager, through
    // ServiceBase), which then cancels the run, and a shutdown leaves the graceful close no time; where
    // a signal stops it (Linux), DesktopHost.HostDaemonStoppedBySignal runs it, and the parser turns the
    // signal into the same cancellation.
    Task<int> HostDaemon(DaemonRun run, CancellationToken cancellationToken);
}
