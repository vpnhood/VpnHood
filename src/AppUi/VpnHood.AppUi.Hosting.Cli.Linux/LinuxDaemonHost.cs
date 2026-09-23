using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Linux;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// The app as Linux hosts a headless one: VpnHoodAppLinux, which takes the single-instance socket
// and opens the command file the stop command writes to. Only root can be it - the tun device, the
// routes, the firewall rules and resolvectl all belong to root (LinuxTunVpnAdapter) - and that is
// said here, in the constructor, rather than let the tun device fail ten layers down, which is
// what someone running "daemon" by hand out of curiosity would otherwise be shown.
public sealed class LinuxDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodAppLinux _appLinux;

    public LinuxDaemonHost(Func<AppOptions> appOptionsFactory, string instanceName)
    {
        if (!LinuxUser.IsRoot)
            throw new InvalidOperationException(
                "The VPN service must run as root: it creates the tunnel device and edits the routing table. " +
                $"Try: sudo systemctl start {instanceName}");

        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _appLinux = VpnHoodAppLinux.Init(appOptionsFactory, ["/nowindow"]);
    }

    // The old adapter, as a previous run's route may still be active.
    public Task Prepare(CancellationToken cancellationToken)
    {
        return _appLinux.PrepareAsync(cancellationToken);
    }

    public void Dispose()
    {
        // Singleton<T> publishes Dispose without implementing IDisposable, hence a call and not a using.
        _appLinux.Dispose();
    }
}
