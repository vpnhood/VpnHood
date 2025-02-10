﻿using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
    //Process = ":vpnhood_process",
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService
{
    private VpnHoodClient? _vpnHoodClient;
    private AndroidVpnNotification? _notification;

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {

        // signal start command
        if (intent?.Action == "connect") {
            return Connect() ? StartCommandResult.Sticky : StartCommandResult.NotSticky;
        }

        if (intent?.Action == "disconnect") {
            _ = Disconnect();
            return StartCommandResult.NotSticky;
        }


        return StartCommandResult.NotSticky;
    }

    public bool Connect()
    {
        try {
            // already connecting
            if (_vpnHoodClient != null)
                return true;

            // create vpn adapter
            var adapter = new AndroidVpnAdapter(this);
            adapter.Disposed += (sender, e) => _ = Disconnect();

            // create vpn client //todo: set tracker
            _vpnHoodClient = VpnHoodClientFactory.Create(adapter, new SocketFactory(), null);

            // initialize notification
            _notification = new AndroidVpnNotification(this, new VpnServiceLocalization(), _vpnHoodClient.SessionName);
            StartForeground(AndroidVpnNotification.NotificationId, _notification.Build());
            _vpnHoodClient.StateChanged += Client_StateChanged;

            // cancellation token will be handled by dispose 
            _ = _vpnHoodClient.Connect();
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
        var client = _vpnHoodClient;
        if (client != null)
            _notification?.Update(client.State);
    }


    private async Task Disconnect()
    {
        if (_vpnHoodClient == null)
            return;

        // stop vpn
        await _vpnHoodClient.DisposeAsync();
        _vpnHoodClient.StateChanged -= Client_StateChanged;
        _vpnHoodClient = null;

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
        if (_vpnHoodClient != null) {
            var disposeTask = _vpnHoodClient.DisposeAsync(sendBye: false).AsTask();
            if (!disposeTask.Wait(TimeSpan.FromSeconds(3))) // Timeout for safety
                VhLogger.Instance.LogWarning("DisposeAsync() took too long, skipping remaining cleanup.");
            _vpnHoodClient.StateChanged -= Client_StateChanged;
            _vpnHoodClient = null;
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
        var handler = new Handler(Looper.MainLooper);
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
