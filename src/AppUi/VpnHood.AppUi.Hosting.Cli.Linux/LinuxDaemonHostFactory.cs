using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.AppUi.Hosting.Cli.Linux.Utils;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

public sealed class LinuxDaemonHostFactory(AppInitParams initParams, LinuxCliPaths paths) : IAppDaemonHostFactory
{
    // Only root can be the service: the tun device, the routes and resolvectl belong to root, which is
    // said here rather than by the tun device failing ten layers down.
    public IAppDaemonHost CreateService()
    {
        if (!LinuxUser.IsRoot)
            throw new InvalidOperationException(
                "The VPN service must run as root: it creates the tunnel device and edits the routing table. " +
                $"Try: sudo systemctl start {paths.InstanceName}");

        return new LinuxDaemonHost(initParams, paths.StoragePath);
    }

    public IAppDaemonHost CreateDev(string storagePath)
    {
        return new LinuxDaemonHost(initParams, storagePath);
    }
}
