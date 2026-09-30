using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.AppUi.Hosting.Cli.Windows.Utils;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

public sealed class WindowsDaemonHostFactory(AppInitParams initParams, WindowsCliPaths paths) : IAppDaemonHostFactory
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
        return new WindowsDaemonHost(initParams, storagePath, lockFolderPath: null);
    }

    // Its lock in its own storage: a run as the person cannot reach the service's.
    public IAppDaemonHost CreateDev(string storagePath)
    {
        return new WindowsDaemonHost(initParams, storagePath, lockFolderPath: storagePath);
    }
}
