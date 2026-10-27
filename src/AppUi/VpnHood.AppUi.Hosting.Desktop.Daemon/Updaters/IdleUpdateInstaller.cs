using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

// The service's half of the automatic update's wait for an idle VPN (the platform's updater, -waitForIdle): once a
// session has ended and the app has stayed idle for the idle wait (DefaultIdleWait), the update the updater downloaded
// meanwhile goes to the installer (DesktopAppUpdaterProvider.InstallDownloadedUpdate), one try per session's end. The
// wait passes over a connect that disconnects first - a server switch - and a person who connects again at once; a
// connect within it puts the install off to the next end. Not at the app's start, idle before always on or anyone has
// connected: the updater's own run at boot sees to that. Disposed before the app, whose disconnect at a stop must
// install nothing.
public sealed class IdleUpdateInstaller : IDisposable
{
    // short of always on's first pause (AppAlwaysOnOptions.RetryPauses, 30 s less a fifth at most), so a session's end
    // under always on installs before always on connects again
    public static readonly TimeSpan DefaultIdleWait = TimeSpan.FromSeconds(10);

    private readonly VpnHoodApp _app;
    private readonly DesktopAppUpdaterProvider _updater;
    private readonly TimeSpan _idleWait;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _waitCts;
    private bool _hasBeenInUse;
    private bool _disposed;

    public IdleUpdateInstaller(VpnHoodApp app, DesktopAppUpdaterProvider updater, TimeSpan idleWait)
    {
        _app = app;
        _updater = updater;
        _idleWait = idleWait;
        app.ConnectionStateChanged += App_ConnectionStateChanged;
    }

    // raised on a thread of its own for each change, so the state is read as it is now
    private void App_ConnectionStateChanged(object? sender, EventArgs e)
    {
        lock (_lock) {
            var connectionState = _app.ConnectionState;
            // the install's own, which ends with this service or leaves the app idle again
            if (_disposed || connectionState == AppConnectionState.Updating)
                return;

            if (connectionState != AppConnectionState.None) {
                _hasBeenInUse = true;
                CancelWait();
                return;
            }

            if (!_hasBeenInUse || _waitCts != null)
                return;

            _waitCts = new CancellationTokenSource();
            _ = InstallAfterWait(_waitCts.Token);
        }
    }

    private async Task InstallAfterWait(CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(_idleWait, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return;
        }

        lock (_lock) {
            if (_disposed || cancellationToken.IsCancellationRequested ||
                _app.ConnectionState != AppConnectionState.None)
                return;

            // the next try waits for the next session's end
            _waitCts?.Dispose();
            _waitCts = null;
            _hasBeenInUse = false;
        }

        await _updater.InstallDownloadedUpdate().Vhc();
    }

    // under the lock
    private void CancelWait()
    {
        _waitCts?.Cancel();
        _waitCts?.Dispose();
        _waitCts = null;
    }

    public void Dispose()
    {
        lock (_lock) {
            _disposed = true;
            CancelWait();
        }

        _app.ConnectionStateChanged -= App_ConnectionStateChanged;
    }
}
