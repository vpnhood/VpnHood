using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop;

// What the platform says for itself: the four answers every command reaches for that are not the
// app's, and the ones only some platforms have. A platform package (Cli.Linux, Cli.Windows) builds
// one of these and hands it to DesktopHost with the head's DesktopInitParams; nothing in this project reads
// a static.
public class DesktopPlatform
{
    public required IAppDesktopPaths Paths { get; init; }
    public required IAppInstanceController Instance { get; init; }

    // Where the service hands its API's address to an administrator.
    public required IDaemonChannel Channel { get; init; }

    // How this OS tells who owns the far end of a loopback connection, checked before a call is sent.
    public required ILoopbackPeerCheck PeerCheck { get; init; }

    // Registering and removing the instance, where the app does it itself (Windows: the service, which
    // "service install" registers); null where the package's installer does (Linux), and then those
    // commands are not offered.
    public IAppInstanceSetup? InstanceSetup { get; init; }

    // The tray that keeps the UI where the platform has one (Windows), started beside the window over
    // the same API: closing the window then only hides it. Where there is none (Linux), closing the
    // window ends the UI. The service runs on either way.
    public Func<DesktopTrayParams, IDisposable>? CreateTray { get; init; }

    // The app for "daemon", which the systemd unit or the service control manager runs, and for "dev".
    public required IAppDaemonHostFactory DaemonHostFactory { get; init; }

    // The console as this platform has it, for the process that holds the app ("daemon", "dev"),
    // which adds it to the log before anything logs: the journal's sink where stdout is the journal
    // (Linux under systemd), a terminal's otherwise.
    public required Func<ILoggerProvider> CreateConsoleLoggerProvider { get; init; }

    // The daemon's run, hosted by a service manager that stops it with a call rather than a signal
    // (Windows: the service control manager, through ServiceBase), which then cancels the run. Null
    // where a signal stops it (Linux), which the parser turns into the same cancellation.
    public Func<Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>? HostDaemon { get; init; }
}
