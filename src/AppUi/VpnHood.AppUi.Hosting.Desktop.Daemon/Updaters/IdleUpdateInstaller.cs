using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

/// <summary>
/// Provides the service's half of the automatic update's wait for an idle VPN: once a session ended and the app stayed
/// idle for the idle wait, the update the updater downloaded meanwhile goes to the installer, one try per session's end.
/// </summary>
/// <remarks>
/// The wait passes over a connect that disconnects first, as a server switch; a connect within it puts the install off
/// to the next end. Nothing installs at the app's start. It is disposed before the app, whose disconnect at a stop must
/// install nothing.
/// </remarks>
public sealed class IdleUpdateInstaller : IDisposable
{
    /// <summary>The default idle wait, 10 seconds: shorter than always-on's default first pause.</summary>
    public static readonly TimeSpan DefaultIdleWait = TimeSpan.FromSeconds(10);

    private readonly VpnHoodApp _app;
    private readonly DesktopAppUpdaterProvider _updater;
    private readonly TimeSpan _idleWait;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _waitCts;
    private bool _hasBeenInUse;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleUpdateInstaller"/> class, which follows the app's state.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <param name="updater">The updater.</param>
    /// <param name="idleWait">How long the app must stay idle after a session's end.</param>
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
            _hasBeenInUse = false;
        }

        // under the wait's token until the installer starts, so a connect or a stop by then puts the install off
        try {
            await _updater.InstallDownloadedUpdate(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            // put off
        }

        lock (_lock) {
            // the wait's own, unless a connect or a stop has ended it
            if (_waitCts is { } waitCts && waitCts.Token == cancellationToken) {
                waitCts.Dispose();
                _waitCts = null;
            }
        }
    }

    // under the lock
    private void CancelWait()
    {
        _waitCts?.Cancel();
        _waitCts?.Dispose();
        _waitCts = null;
    }

    /// <inheritdoc/>
    /// <remarks>It stops following the app's state.</remarks>
    public void Dispose()
    {
        lock (_lock) {
            _disposed = true;
            CancelWait();
        }

        _app.ConnectionStateChanged -= App_ConnectionStateChanged;
    }
}
