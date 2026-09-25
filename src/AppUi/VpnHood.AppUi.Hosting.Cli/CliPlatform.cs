namespace VpnHood.AppUi.Hosting.Cli;

// What the platform says for itself: the two answers every command reaches for that are not the
// app's, and the ones only some platforms have. A platform package (Cli.Linux, Cli.Windows) builds
// one of these and hands it to CliHost with the head's CliHeadParams; nothing in this project reads
// a static.
public class CliPlatform
{
    public required IAppCliPaths Paths { get; init; }
    public required IAppInstanceController Instance { get; init; }

    // Registering and removing the instance, where the app does it itself (Windows: the service, which
    // "service install" registers); null where the package's installer does (Linux), and then those
    // commands are not offered.
    public IAppInstanceSetup? InstanceSetup { get; init; }

    // The tray that keeps the UI where the platform has one (Windows), started beside the window over
    // the same API: closing the window then only hides it. Where there is none (Linux), closing the
    // window ends the UI. The service runs on either way.
    public Func<DesktopTrayParams, IDisposable>? CreateTray { get; init; }

    // How to BE the daemon, for a platform whose instance is a headless process this binary runs
    // (Linux: the systemd unit calls "daemon"; Windows: the service control manager does). Null on a
    // platform whose instance is something else, and then there is no "daemon" command to type,
    // which is right: it would have nothing to be.
    public Func<IAppDaemonHost>? CreateDaemonHost { get; init; }

    // The same app for a debugger (the "dev" command), in the storage it is handed and as whoever runs
    // it: a tunnel then needs an administrator or root, unless DebugData1 has /null-capture.
    public Func<string, IAppDaemonHost>? CreateDevDaemonHost { get; init; }

    // The daemon's run, hosted by a service manager that stops it with a call rather than a signal
    // (Windows: the service control manager, through ServiceBase), which then cancels the run. Null
    // where a signal stops it (Linux), which the parser turns into the same cancellation.
    public Func<Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>? HostDaemon { get; init; }
}
