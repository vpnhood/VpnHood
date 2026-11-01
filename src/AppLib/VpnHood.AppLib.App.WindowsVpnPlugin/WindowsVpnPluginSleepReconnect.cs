using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// The VPN back after a sleep, while the app runs: Windows ends every RAS connection, the plug-in's too, as the machine
// sleeps or hibernates (RAS 832, 833) or enters Modern Standby (631), and dials none at the wake. A sleep that came with
// the VPN up is noted at its suspend; after the wake, once the screen is on, the app connects again. A wake no person
// made - a timer's - keeps the screen off and soon sleeps again, maybe by a hibernate out of Modern Standby, which takes
// Windows' teardown of a VPN dialed meanwhile half-way: the dial after that wake hung in Windows. So the connect waits
// for the person, through more sleeps if need be. Once the VPN is down - Windows may still be ending it - the app
// connects, and again every 5 s for 2 minutes while that fails, the network still coming back; only the last try's
// failure is shown, as an earlier one's would stand over the VPN the next try connects. A VPN that outlived the sleep,
// a connect the person makes meanwhile, or one cancelled, ends it.
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
    private volatile bool _isDisplayOn = true; // until Windows tells, at the registration
    private int _isPending; // a wake after a sleep that ended the VPN, whose connect waits for the screen
    private int _isReconnecting; // a wake notice while one runs is of the wake it connects after

    public WindowsVpnPluginSleepReconnect(VpnHoodApp app)
    {
        _app = app;
        _sleepWatch = new WindowsVpnPluginSleepWatch(onSuspend: () => _wasUpAtSuspend = _isUp, onWake: OnWake,
            onDisplayChanged: OnDisplayChanged);
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
        if (Volatile.Read(ref _isReconnecting) == 1)
            return;

        Volatile.Write(ref _isPending, 1);
        if (!_isDisplayOn)
            VhLogger.Instance.LogInformation(
                "The machine woke with its screen off, so the app connects the VPN again once the screen is on.");

        TryStartReconnect();
    }

    private void OnDisplayChanged(bool isDisplayOn)
    {
        _isDisplayOn = isDisplayOn;
        if (isDisplayOn)
            TryStartReconnect();
    }

    private void TryStartReconnect()
    {
        if (!_isDisplayOn || Interlocked.CompareExchange(ref _isReconnecting, 1, 0) != 0)
            return;

        if (Interlocked.Exchange(ref _isPending, 0) == 0) {
            Volatile.Write(ref _isReconnecting, 0);
            return;
        }

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
                var isRetried = DateTime.UtcNow < deadline; // the first try after the 2 minutes is the last
                try {
                    await _app.Connect(uiContext: null, new ConnectOptions { IsRetried = isRetried }, cancellationToken).Vhc();
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && isRetried) {
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
        finally {
            Volatile.Write(ref _isReconnecting, 0);
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
