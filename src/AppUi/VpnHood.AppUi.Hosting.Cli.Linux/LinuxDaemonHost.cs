using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Linux;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// The app as Linux hosts a headless one, or a debugger does: VpnHoodLinuxApp, which takes the
// single-instance lock on the app id before anything is built and clears what a previous run left.
public sealed class LinuxDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodLinuxApp _linuxApp;

    private LinuxDaemonHost(AppInitParams initParams, string storagePath)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _linuxApp = VpnHoodLinuxApp.Init(initParams, storagePath);
    }

    // The service, in the storage that belongs to it. Only root can be it - the tun device, the
    // routes, the firewall rules and resolvectl all belong to root (LinuxTunVpnAdapter) - and that is
    // said here rather than let the tun device fail ten layers down, which is what someone running
    // "daemon" by hand out of curiosity would otherwise be shown.
    public static LinuxDaemonHost CreateService(AppInitParams initParams, LinuxCliPaths paths)
    {
        if (!LinuxUser.IsRoot)
            throw new InvalidOperationException(
                "The VPN service must run as root: it creates the tunnel device and edits the routing table. " +
                $"Try: sudo systemctl start {paths.InstanceName}");

        return new LinuxDaemonHost(initParams, paths.StoragePath);
    }

    // A debugger's (CliPlatform.CreateDevDaemonHost), in the storage it is handed, as whoever runs it.
    public static LinuxDaemonHost CreateDev(AppInitParams initParams, string storagePath)
    {
        return new LinuxDaemonHost(initParams, storagePath);
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        return _linuxApp.DisposeAsync();
    }
}
