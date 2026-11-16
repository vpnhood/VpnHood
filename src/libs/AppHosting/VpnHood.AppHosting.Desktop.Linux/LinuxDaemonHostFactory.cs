using VpnHood.AppLogic.App;
using VpnHood.AppLogic.App.Linux;
using VpnHood.AppHosting.Desktop.Daemon.Platform;
using VpnHood.AppHosting.Desktop.Linux.Utils;
using VpnHood.AppHosting.Desktop.Platform;

namespace VpnHood.AppHosting.Desktop.Linux;

internal sealed class LinuxDaemonHostFactory(AppInitParams initParams, LinuxDesktopPaths paths) : IAppDaemonHostFactory
{
    // Only root can be the service: the tun device, the routes and resolvectl belong to root, which is
    // said here rather than by the tun device failing ten layers down. Its lock is in its folder
    // under /run, where the channel's socket is.
    public IAppDaemonHost CreateService()
    {
        if (!LinuxUser.IsRoot)
            throw new InvalidOperationException(
                "The VPN service must run as root: it creates the tunnel device and edits the routing table. " +
                $"Try: sudo systemctl start {paths.InstanceName}");

        var appPaths = new VpnHoodLinuxAppPaths {
            StoragePath = paths.StoragePath,
            LockFolderPath = paths.RuntimePath,
            KillSwitchOffScriptPath = paths.KillSwitchOffScriptPath,
            VpnHostPath = paths.VpnHostPath
        };
        return new LinuxDaemonHost(initParams, appPaths, isService: true);
    }

    // Its lock in its own storage: a run as the person cannot write the service's folder. It starts the VPN host as
    // the service does: the head compiles against nothing of the VPN service's side.
    public IAppDaemonHost CreateDev(string storagePath)
    {
        var appPaths = new VpnHoodLinuxAppPaths {
            StoragePath = storagePath,
            LockFolderPath = storagePath,
            KillSwitchOffScriptPath = paths.KillSwitchOffScriptPath,
            VpnHostPath = paths.VpnHostPath
        };
        return new LinuxDaemonHost(initParams, appPaths, isService: false);
    }
}
