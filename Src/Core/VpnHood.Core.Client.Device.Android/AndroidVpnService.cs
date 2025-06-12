using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.AndroidTun;
using Environment = System.Environment;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
// #if !DEBUG 
    Process = ProcessName,
//#endif
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService, IVpnServiceHandler
{
    private readonly VpnServiceHost _vpnServiceHost;
    private AndroidVpnNotification? _notification;
    public const string ProcessName = ":vpnhood_process";

    public static string VpnServiceConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    public AndroidVpnService()
    {
        Thread.Sleep(4000); //todo
        _vpnServiceHost = new VpnServiceHost(VpnServiceConfigFolder, this, new SocketFactory());

    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        VhLogger.Instance.LogInformation("AndroidVpnService OnStartCommand. Action: {Action}", action);

        // get "manual" in 
        switch (action) {
            // signal start command
            case null or "android.net.VpnService" or "connect":
                _ = _vpnServiceHost.TryConnect(forceReconnect: action == "connect");
                return StartCommandResult.Sticky;

            case "disconnect":
                _ = _vpnServiceHost.TryDisconnect();
                return StartCommandResult.NotSticky;

            default:
                return StartCommandResult.NotSticky;
        }
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        return new AndroidVpnAdapter(this, new AndroidVpnAdapterSettings {
            AdapterName = adapterSettings.AdapterName, 
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            QueueCapacity = adapterSettings.QueueCapacity,
            AutoMetric = adapterSettings.AutoMetric
        });
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
        if (_notification == null) {
            _notification = new AndroidVpnNotification(this, new VpnServiceLocalization(), connectionInfo.SessionName ?? "VPN");
            StartForeground(AndroidVpnNotification.NotificationId, _notification.Build());
        }

        _notification.Update(connectionInfo.ClientState);
    }

    public void StopNotification()
    {
        if (_notification == null)
            return;

        VhLogger.Instance.LogDebug("Remove VpnService from foreground and stop the notification.");
        StopForeground(StopForegroundFlags.Remove);

        // clear notification
        _notification?.Dispose();
        _notification = null;
    }

    public override void OnDestroy()
    {
        VhLogger.Instance.LogDebug("VpnService is destroying.");

        StopNotification();
        
        _vpnServiceHost.Dispose();
        base.OnDestroy();
    }
}
