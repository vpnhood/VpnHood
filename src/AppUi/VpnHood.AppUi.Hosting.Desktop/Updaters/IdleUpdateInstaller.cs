using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Updaters;

// The service's half of the automatic update's wait for an idle VPN (the platform's updater, -waitForIdle): once a
// session has ended and the app is idle again, the update the updater downloaded meanwhile goes to the installer
// (DesktopAppUpdaterProvider.InstallDownloadedUpdate), one try at a time. Not at the app's start, idle before always
// on or anyone has connected: the updater's own run at boot sees to that. Disposed before the app, whose disconnect
// at a stop must install nothing.
public sealed class IdleUpdateInstaller : IDisposable
{
    private readonly VpnHoodApp _app;
    private readonly DesktopAppUpdaterProvider _updater;
    private volatile bool _hasBeenInUse;
    private volatile bool _disposed;
    private int _isInstalling;

    public IdleUpdateInstaller(VpnHoodApp app, DesktopAppUpdaterProvider updater)
    {
        _app = app;
        _updater = updater;
        app.ConnectionStateChanged += App_ConnectionStateChanged;
    }

    // raised on a thread of its own for each change, so the state is read as it is now
    private void App_ConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        if (_app.ConnectionState != AppConnectionState.None) {
            _hasBeenInUse = true;
            return;
        }

        if (_hasBeenInUse && Interlocked.Exchange(ref _isInstalling, 1) == 0)
            _ = Install();
    }

    private async Task Install()
    {
        try {
            await _updater.InstallDownloadedUpdate().Vhc();
        }
        finally {
            Volatile.Write(ref _isInstalling, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _app.ConnectionStateChanged -= App_ConnectionStateChanged;
    }
}
