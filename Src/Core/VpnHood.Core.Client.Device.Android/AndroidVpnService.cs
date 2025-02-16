using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Runtime;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServicing;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling.Factory;
using Environment = System.Environment;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
    Process = ":vpnhood_process",
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService, IVpnServiceHandler
{
    private readonly VpnHoodService _vpnHoodService;
    private AndroidVpnNotification? _notification;

    public static string VpnServiceConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    public AndroidVpnService()
    {
        _vpnHoodService = new VpnHoodService(VpnServiceConfigFolder, this, new SocketFactory());
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        switch (intent?.Action) {
            // signal start command
            case "connect":
                return _vpnHoodService.Connect()
                    ? StartCommandResult.Sticky
                    : StartCommandResult.NotSticky;

            case "disconnect":
                _vpnHoodService.Disconnect();
                return StartCommandResult.NotSticky;

            default:
                return StartCommandResult.NotSticky;
        }
    }
    public ITracker? CreateTracker()
    {
        return null;
    }

    public IVpnAdapter CreateAdapter()
    {
        return new AndroidVpnAdapter(this);
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
        if (_notification == null) {
            _notification = new AndroidVpnNotification(this, new VpnServiceLocalization(),
                connectionInfo.SessionInfo?.SessionName ?? "VPN");
            StartForeground(AndroidVpnNotification.NotificationId, _notification.Build());
        }

        _notification.Update(connectionInfo.ClientState);
    }

    public void StopNotification()
    {
        StopForeground(StopForegroundFlags.Remove);

        // clear notification
        _notification?.Dispose();
        _notification = null;
    }

    public override void OnDestroy()
    {
        VhLogger.Instance.LogTrace("VpnService is destroying.");
        _ = _vpnHoodService.DisposeAsync();

        StopNotification(); 
        base.OnDestroy();
    }
}
