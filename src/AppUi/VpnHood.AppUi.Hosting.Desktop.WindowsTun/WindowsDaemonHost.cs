using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// The app as the Windows service runs it, or a debugger does: VpnHoodWindowsApp, which takes the
// single-instance lock on the app id before anything is built - the service's where only
// administrators reach, a debugger's in the folder it is handed. The service's, in an install, also
// hands the updater's download back once the VPN is idle again, and runs the updater again at a
// connect when its last run could not reach the server - by the app's own updater, the notice's, so
// that both installs are the one the app refuses connects during.
internal sealed class WindowsDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodWindowsApp _windowsApp;
    private readonly IdleUpdateInstaller? _idleUpdateInstaller;
    private readonly MissedUpdateRetrier? _missedUpdateRetrier;

    public WindowsDaemonHost(AppInitParams initParams, string storagePath, string? lockFolderPath, bool isService)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _windowsApp = VpnHoodWindowsApp.Init(initParams, storagePath, lockFolderPath);
        if (isService && VpnHoodApp.Instance.Services.UpdaterService?.UpdaterProvider is DesktopAppUpdaterProvider updater) {
            _idleUpdateInstaller =
                new IdleUpdateInstaller(VpnHoodApp.Instance, updater, IdleUpdateInstaller.DefaultIdleWait);
            _missedUpdateRetrier = new MissedUpdateRetrier(VpnHoodApp.Instance, updater);
        }
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        _idleUpdateInstaller?.Dispose();
        _missedUpdateRetrier?.Dispose();
        return _windowsApp.DisposeAsync();
    }

    // The stop whose time has run out: the app at once, which releases the adapter.
    public void Dispose()
    {
        _idleUpdateInstaller?.Dispose();
        _missedUpdateRetrier?.Dispose();
        _windowsApp.Dispose();
    }
}
