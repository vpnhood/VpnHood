using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.AppUi.Hosting.Desktop.Updaters;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// The app as the Windows service runs it, or a debugger does: VpnHoodWindowsApp, which takes the
// single-instance lock on the app id before anything is built - the service's where only
// administrators reach, a debugger's in the folder it is handed. The service's, in an install, also
// hands the updater's download back once the VPN is idle again.
internal sealed class WindowsDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodWindowsApp _windowsApp;
    private readonly IdleUpdateInstaller? _idleUpdateInstaller;

    public WindowsDaemonHost(AppInitParams initParams, string storagePath, string? lockFolderPath,
        WindowsAppUpdaterProvider? updater)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _windowsApp = VpnHoodWindowsApp.Init(initParams, storagePath, lockFolderPath);
        _idleUpdateInstaller = updater is null ? null : new IdleUpdateInstaller(VpnHoodApp.Instance, updater);
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        _idleUpdateInstaller?.Dispose();
        return _windowsApp.DisposeAsync();
    }

    // The stop whose time has run out: the app at once, which releases the adapter.
    public void Dispose()
    {
        _idleUpdateInstaller?.Dispose();
        _windowsApp.Dispose();
    }
}
