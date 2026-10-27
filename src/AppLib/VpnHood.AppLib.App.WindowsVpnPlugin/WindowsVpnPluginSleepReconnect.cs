using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// The VPN back after a sleep, while the app runs: Windows ends every RAS connection, the plug-in's too, as the machine
// sleeps or hibernates (RAS 832, 833), and dials none at the wake. A sleep that came with the VPN up is noted at its
// suspend; at the wake, once the VPN is down - Windows may still be ending it - the app connects again, and again every
// 5 s for 2 minutes while that fails, the network still coming back. A VPN that outlived the sleep, a connect the
// person makes meanwhile, or one cancelled, ends it. A Modern Standby, which may tell no suspend, is not caught.
internal sealed class WindowsVpnPluginSleepReconnect : IDisposable
{
    private static readonly TimeSpan DownWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryTime = TimeSpan.FromMinutes(2);
    private readonly VpnHoodApp _app;
    private readonly WindowsVpnPluginSleepWatch _sleepWatch;
    private readonly CancellationTokenSource _disposeCts = new();
    private volatile bool _isUp;
    private volatile bool _wasUpAtSuspend;

    public WindowsVpnPluginSleepReconnect(VpnHoodApp app)
    {
        _app = app;
        _sleepWatch = new WindowsVpnPluginSleepWatch(onSuspend: () => _wasUpAtSuspend = _isUp, onWake: OnWake);
        _isUp = IsUp(app.ConnectionState);
        app.ConnectionStateChanged += OnConnectionStateChanged;
    }

    // a connect on its way or done; not one going down, nor an update about to stop the app
    private static bool IsUp(AppConnectionState state) =>
        state is not (AppConnectionState.None or AppConnectionState.Disconnecting or AppConnectionState.Updating);

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        _isUp = IsUp(_app.ConnectionState);
    }

    private void OnWake()
    {
        if (!_wasUpAtSuspend)
            return;

        _wasUpAtSuspend = false;
        _ = Reconnect(_disposeCts.Token);
    }

    private async Task Reconnect(CancellationToken cancellationToken)
    {
        try {
            var downDeadline = DateTime.UtcNow + DownWait;
            while (!_app.IsIdle) {
                if (DateTime.UtcNow > downDeadline) {
                    VhLogger.Instance.LogInformation("The VPN outlived the machine's sleep, so it is not connected again.");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
            }

            VhLogger.Instance.LogInformation("The machine woke with its VPN ended by the sleep, so the app connects again.");
            var deadline = DateTime.UtcNow + RetryTime;
            while (true) {
                try {
                    await _app.Connect(uiContext: null, connectOptions: null, cancellationToken).Vhc();
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && DateTime.UtcNow < deadline) {
                    VhLogger.Instance.LogWarning(ex, "The connect after the wake failed; the app tries again.");
                }

                await Task.Delay(RetryDelay, cancellationToken).Vhc();
                if (!_app.IsIdle)
                    return; // the person connected meanwhile
            }
        }
        catch (OperationCanceledException) {
            // the app ending, or the person's disconnect of the connect after the wake
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not connect the VPN again after the machine's sleep.");
        }
    }

    public void Dispose()
    {
        _app.ConnectionStateChanged -= OnConnectionStateChanged;
        _sleepWatch.Dispose();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
