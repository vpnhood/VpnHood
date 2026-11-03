using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun.Utils;
using VpnHood.Core.Client.Devices.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

public sealed class WindowsDaemonHostFactory(
    AppInitParams initParams,
    WindowsDesktopPaths paths,
    IInProcessVpnServiceFactory inProcessVpnServiceFactory)
    : IAppDaemonHostFactory
{
    // Only an administrator can be the service: the adapter, the routes and the storage's own ACL all
    // need one, which is said here rather than by the adapter failing ten layers down.
    public IAppDaemonHost CreateService()
    {
        if (!WindowsElevation.IsElevated)
            throw new InvalidOperationException(
                "The VPN service must run as a service or as an administrator: it creates the network adapter and edits the routes. " +
                $"Try: {paths.CommandName} service start");

        var appPaths = new VpnHoodWindowsAppPaths {
            StoragePath = WindowsServiceStorage.Prepare(paths),
            ServiceName = paths.InstanceName,
            VpnHostPath = paths.VpnHostPath
        };
        var daemonHost = new WindowsDaemonHost(initParams, appPaths, isService: true);

        // the windows and trays an update's setup closed, open again now that the service is up to answer them
        WindowsUiRelaunch.Run(paths);
        return daemonHost;
    }

    // Its lock in its own storage: a run as the person cannot reach the service's. The VPN service runs in this
    // process too, so one debugger run reaches the tunnel.
    public IAppDaemonHost CreateDev(string storagePath)
    {
        var appPaths = new VpnHoodWindowsAppPaths {
            StoragePath = storagePath,
            LockFolderPath = storagePath,
            InProcessVpnServiceFactory = inProcessVpnServiceFactory
        };
        return new WindowsDaemonHost(initParams, appPaths, isService: false);
    }
}
