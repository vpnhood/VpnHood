using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// The platform, as far as a connection asks it: its parts, and whether whoever runs this may use the
// app. It has no tray, no logs and no service manager of its own.
internal sealed class TestDesktopPlatform : IDesktopPlatform
{
    public bool IsUserAdministrator { get; init; } = true;

    public IAppDesktopPaths Paths { get; } = new TestDesktopPaths();
    public IAppInstanceController Instance { get; } = new TestInstanceController();
    public IDaemonChannel Channel { get; } = new TestDaemonChannel();
    public ILoopbackPeerCheck PeerCheck { get; } = new TestLoopbackPeerCheck();
    public IAppInstanceSetup? InstanceSetup => null;
    public IAppDaemonHostFactory DaemonHostFactory { get; } = new TestDaemonHostFactory();
    public bool IsTraySupported => false;

    public bool IsAdministrator() => IsUserAdministrator;
    public IDisposable? CreateTray(DesktopTrayParams trayParams) => null;
    public ILoggerProvider CreateConsoleLoggerProvider() => throw new NotSupportedException();
    public ILoggerProvider CreateSystemLogLoggerProvider() => throw new NotSupportedException();

    public Task<int> HostDaemon(Func<CancellationToken, Task<int>> run, CancellationToken cancellationToken) =>
        run(cancellationToken);
}
