using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

// The service's half of the automatic update's retry: at each connect, an updater run that could not reach the server
// is made again, now through the tunnel (DesktopAppUpdaterProvider.RetryMissedUpdate), one try at a time. A connect is
// the change into Connected, a reconnect's included. Disposed before the app.
public sealed class MissedUpdateRetrier : IDisposable
{
    private readonly VpnHoodApp _app;
    private readonly DesktopAppUpdaterProvider _updater;
    private int _isConnected;
    private int _isRetrying;
    private volatile bool _disposed;

    public MissedUpdateRetrier(VpnHoodApp app, DesktopAppUpdaterProvider updater)
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

        var isConnected = _app.ConnectionState == AppConnectionState.Connected;
        var wasConnected = Interlocked.Exchange(ref _isConnected, isConnected ? 1 : 0) == 1;
        if (isConnected && !wasConnected && Interlocked.Exchange(ref _isRetrying, 1) == 0)
            _ = Retry();
    }

    private async Task Retry()
    {
        try {
            await _updater.RetryMissedUpdate().Vhc();
        }
        finally {
            Volatile.Write(ref _isRetrying, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _app.ConnectionStateChanged -= App_ConnectionStateChanged;
    }
}
