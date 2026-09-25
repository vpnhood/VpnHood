using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Windows;
using VpnHood.AppUi.Hosting.Cli.Windows.Internal;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// The app as the Windows service runs it, or a debugger does: VpnHoodWindowsApp, which takes the
// single-instance lock on the app id before anything is built.
public sealed class WindowsDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodWindowsApp _windowsApp;

    private WindowsDaemonHost(AppInitParams initParams, string storagePath)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _windowsApp = VpnHoodWindowsApp.Init(initParams, storagePath);
    }

    // The service, in the storage that belongs to it. Only an administrator can be it - LocalSystem,
    // when the service control manager starts it: the adapter, the routes and the storage's own ACL
    // all need one, and that is said here rather than let the adapter fail ten layers down.
    public static WindowsDaemonHost CreateService(AppInitParams initParams, WindowsCliPaths paths)
    {
        if (!WindowsElevation.IsElevated)
            throw new InvalidOperationException(
                "The VPN service must run as a service or as an administrator: it creates the network adapter and edits the routes. " +
                $"Try: {paths.CommandName} service start");

        WindowsServiceStorage.Secure(paths.StoragePath);
        return new WindowsDaemonHost(initParams, paths.StoragePath);
    }

    // A debugger's (CliPlatform.CreateDevDaemonHost), in the storage it is handed, as whoever runs it.
    public static WindowsDaemonHost CreateDev(AppInitParams initParams, string storagePath)
    {
        return new WindowsDaemonHost(initParams, storagePath);
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        return _windowsApp.DisposeAsync();
    }
}
