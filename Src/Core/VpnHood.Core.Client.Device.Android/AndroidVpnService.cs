using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling.Factory;
using Environment = System.Environment;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
    //Process = ":vpnhood_process",
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService
{
    private VpnHoodService? _vpnHoodService;
    private AndroidVpnNotification? _notification;

    public static string VpnServiceConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {

        // signal start command
        if (intent?.Action == "connect") {
            _ = Connect();
            return StartCommandResult.Sticky;
        }

        if (intent?.Action == "disconnect") {
            _ = Disconnect();
            return StartCommandResult.NotSticky;
        }


        return StartCommandResult.NotSticky;
    }

    public async Task<bool> Connect()
    {
        try {
            // already connecting
            if (_vpnHoodService != null)
                return true;

            // create vpn adapter
            var serviceContext = new VpnHoodServiceContext(VpnServiceConfigFolder);
            var clientOptions = serviceContext.ReadClientOptions();
            IVpnAdapter adapter = clientOptions.UseNullCapture ? new NullVpnAdapter() : new AndroidVpnAdapter(this);

            // create vpn client //todo: set tracker
            _vpnHoodService = await VpnHoodService.Create(serviceContext, adapter, new SocketFactory(), null);

            // initialize notification
            _notification = new AndroidVpnNotification(this, new VpnServiceLocalization(), _vpnHoodService.Client.SessionName);
            StartForeground(AndroidVpnNotification.NotificationId, _notification.Build());
            _vpnHoodService.Client.StateChanged += Client_StateChanged;

            // cancellation token will be handled by dispose 
            _ = _vpnHoodService.Client.Connect();
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in Connect of VpnService.");
            // toasts are not allowed in service
            return false;
        }
    }

    private void Client_StateChanged(object? sender, EventArgs e)
    {
        var vpnHoodService = _vpnHoodService;
        if (vpnHoodService == null) 
            return;

        // update notification
        _notification?.Update(vpnHoodService.Client.State);

        // disconnect when disposed
        if (vpnHoodService.Client.State == ClientState.Disposed)
            _ = Disconnect();
    }


    private async Task Disconnect()
    {
        if (_vpnHoodService == null)
            return;

        // stop vpn
        await _vpnHoodService.DisposeAsync();
        _vpnHoodService.Client.StateChanged -= Client_StateChanged;
        _vpnHoodService = null;

        // clear notification
        _notification?.Dispose();
        _notification = null;

        // stop service
        try {
            StopSelf();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in StopSelf of VpnService.");
        }
    }

    public override void OnDestroy()
    {
        VhLogger.Instance.LogTrace("VpnService is destroying.");

        // can not call Disconnect() because it will call StopSelf, also disconnect does not have timeout
        if (_vpnHoodService != null) {
            var disposeTask = _vpnHoodService.DisposeAsync(waitForBye: false).AsTask();
            if (!disposeTask.Wait(TimeSpan.FromSeconds(3))) // Timeout for safety
                VhLogger.Instance.LogWarning("DisposeAsync() took too long, skipping remaining cleanup.");
            _vpnHoodService.Client.StateChanged -= Client_StateChanged;
            _vpnHoodService = null;
        }

        // clear notification
        _notification?.Dispose();
        _notification = null;

        try {
            // it must be after _mInterface.Close
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in StopForeground of VpnService.");
        }

        base.OnDestroy();
    }

    // todo: check it
    private void ShowToast(string message)
    {
        var handler = new Handler(Looper.MainLooper!);
        handler.Post(() =>
        {
            try {
                Toast.MakeText(Application.Context, message, ToastLength.Short)?.Show();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error showing Toast in VpnService.");
            }
        });
    }


}
