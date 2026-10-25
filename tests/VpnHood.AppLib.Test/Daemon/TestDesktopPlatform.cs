using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.AppUi.Hosting.Desktop;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppLib.Test.Daemon;

// The platform, as far as a connection or the daemon asks it: its parts, and whether whoever runs this
// may use the app. It has no logs, and the test is its service manager: it stops the daemon, and ends
// the time the stop leaves the app's graceful close (QuickCloseToken).
internal sealed class TestDesktopPlatform : IDesktopPlatform
{
    public bool IsUserAdministrator { get; init; } = true;
    public CancellationToken QuickCloseToken { get; init; }

    public IAppDesktopPaths Paths { get; } = new TestDesktopPaths();
    public IAppInstanceController Instance { get; } = new TestInstanceController();
    public IDaemonChannel Channel { get; init; } = new TestDaemonChannel();
    public ILoopbackPeerCheck PeerCheck { get; } = new TestLoopbackPeerCheck();
    public IAppInstanceSetup? InstanceSetup => null;
    public IAppDaemonHostFactory DaemonHostFactory { get; init; } = new TestDaemonHostFactory();

    public bool IsAdministrator() => IsUserAdministrator;
    public ILoggerProvider CreateConsoleLoggerProvider() => NullLoggerProvider.Instance;
    public ILoggerProvider CreateSystemLogLoggerProvider() => throw new NotSupportedException();

    public Task<int> HostDaemon(DaemonRun run, CancellationToken cancellationToken) =>
        run(cancellationToken, QuickCloseToken);

    public void StartUiAgain(bool startHidden) => throw new NotSupportedException();
}
