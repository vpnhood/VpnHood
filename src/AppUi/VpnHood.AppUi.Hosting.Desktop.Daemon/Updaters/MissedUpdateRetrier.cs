using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

/// <summary>
/// Provides the service's half of the automatic update's retry: after it sees a change into Connected, an updater run
/// that could not reach the server is made again, one try at a time.
/// </summary>
/// <remarks>
/// A connect is the change into Connected, a reconnect's included; one that comes while a retry runs is not seen.
/// Nothing routes the updater through the tunnel itself. It is disposed before the app.
/// </remarks>
public sealed class MissedUpdateRetrier : IDisposable
{
    private readonly VpnHoodApp _app;
    private readonly DesktopAppUpdaterProvider _updater;
    private int _isConnected;
    private int _isRetrying;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissedUpdateRetrier"/> class, which follows the app's state.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <param name="updater">The updater.</param>
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
            _ = Retry(CancellationToken.None);
    }

    private async Task Retry(CancellationToken cancellationToken)
    {
        try {
            await _updater.RetryMissedUpdate(cancellationToken).Vhc();
        }
        finally {
            Volatile.Write(ref _isRetrying, 0);
        }
    }

    /// <inheritdoc/>
    /// <remarks>It stops following the app's state.</remarks>
    public void Dispose()
    {
        _disposed = true;
        _app.ConnectionStateChanged -= App_ConnectionStateChanged;
    }
}
