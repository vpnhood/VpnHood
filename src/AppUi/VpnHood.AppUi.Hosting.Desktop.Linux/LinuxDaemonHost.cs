using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Linux;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.AppUi.Hosting.Desktop.Updaters;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// The app as Linux hosts a headless one, or a debugger does: VpnHoodLinuxApp, which takes the
// single-instance lock on the app id before anything is built and clears what a previous run left.
// The service's, in an install, also hands the updater's download back once the VPN is idle again, and runs the
// updater again at a connect when its last run could not reach the server - by the app's own updater, the notice's,
// so that both installs are the one the app refuses connects during.
internal sealed class LinuxDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodLinuxApp _linuxApp;
    private readonly IdleUpdateInstaller? _idleUpdateInstaller;
    private readonly MissedUpdateRetrier? _missedUpdateRetrier;

    public LinuxDaemonHost(AppInitParams initParams, string storagePath, string lockFolderPath, bool isService)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _linuxApp = VpnHoodLinuxApp.Init(initParams, storagePath, lockFolderPath);
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
        return _linuxApp.DisposeAsync();
    }

    // The stop whose time has run out: the app at once, which releases the adapter.
    public void Dispose()
    {
        _idleUpdateInstaller?.Dispose();
        _missedUpdateRetrier?.Dispose();
        _linuxApp.Dispose();
    }
}
