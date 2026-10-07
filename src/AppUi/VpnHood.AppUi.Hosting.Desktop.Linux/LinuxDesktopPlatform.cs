using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Linux;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// The machine facts Linux answers. No tray keeps a hidden window, so a closed one is gone; the
// installer registers the unit; systemd's signal stops the daemon, which the parser turns into the
// run's cancellation; the daemon's console is the journal where stdout is the journal, so no line
// reaches it twice; and the window writes to the journal's socket, under the instance's name, which
// "service log" reads beside the unit.
public sealed class LinuxDesktopPlatform : IDesktopPlatform
{
    private readonly LinuxDesktopPaths _paths;

    public LinuxDesktopPlatform(DesktopInitParams initParams)
    {
        _paths = new LinuxDesktopPaths(initParams.AppId);
        Instance = new LinuxInstanceController(_paths);
        Channel = new LinuxDaemonChannel(_paths);
        DaemonHostFactory = new LinuxDaemonHostFactory(initParams, _paths);
    }

    public IAppDesktopPaths Paths => _paths;
    public IAppInstanceController Instance { get; }
    public IDaemonChannel Channel { get; }
    public ILoopbackPeerCheck PeerCheck { get; } = new LinuxLoopbackPeerCheck();
    public IAppInstanceSetup? InstanceSetup => null;
    public IAppDaemonHostFactory DaemonHostFactory { get; }
    public bool IsTraySupported => false;

    public bool IsAdministrator() => LinuxAdministrators.IsCurrentUser();

    public IDisposable? CreateTray(DesktopTrayParams trayParams) => null;

    public ILoggerProvider CreateConsoleLoggerProvider() => LinuxJournalLogger.IsConsole
        ? new LinuxJournalLoggerProvider()
        : new ConsoleLoggerProvider();

    public ILoggerProvider CreateSystemLogLoggerProvider() => new LinuxJournalSocketLoggerProvider(_paths.InstanceName);

    public Task<int> HostDaemon(Func<CancellationToken, Task<int>> run, CancellationToken cancellationToken) =>
        run(cancellationToken);
}
