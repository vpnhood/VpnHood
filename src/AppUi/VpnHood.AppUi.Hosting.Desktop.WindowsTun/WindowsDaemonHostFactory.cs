using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

public sealed class WindowsDaemonHostFactory(AppInitParams initParams, WindowsDesktopPaths paths) : IAppDaemonHostFactory
{
    // Only an administrator can be the service: the adapter, the routes and the storage's own ACL all
    // need one, which is said here rather than by the adapter failing ten layers down.
    public IAppDaemonHost CreateService()
    {
        if (!WindowsElevation.IsElevated)
            throw new InvalidOperationException(
                "The VPN service must run as a service or as an administrator: it creates the network adapter and edits the routes. " +
                $"Try: {paths.CommandName} service start");

        var storagePath = WindowsServiceStorage.Prepare(paths);
        var daemonHost = new WindowsDaemonHost(initParams, storagePath, lockFolderPath: null, isService: true);

        // the windows and trays an update's setup closed, open again now that the service is up to answer them
        WindowsUiRelaunch.Run(paths);
        return daemonHost;
    }

    // Its lock in its own storage: a run as the person cannot reach the service's.
    public IAppDaemonHost CreateDev(string storagePath)
    {
        return new WindowsDaemonHost(initParams, storagePath, lockFolderPath: storagePath, isService: false);
    }
}
