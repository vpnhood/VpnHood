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
using Environment = System.Environment;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
//#if !DEBUG
    Process = ":vpnhood_process",
//#endif
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService, IVpnServiceHandler
{
    private readonly VpnServiceHost _vpnServiceHost;
    private AndroidVpnNotification? _notification;

    public static string VpnServiceConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    public AndroidVpnService()
    {
        _vpnServiceHost = new VpnServiceHost(VpnServiceConfigFolder, this, new SocketFactory());
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;

        // get "manual" in 
        switch (action) {
            // signal start command
            case null or "android.net.VpnService" or "connect":
                return _vpnServiceHost.Connect(forceReconnect: action == "connect")
                    ? StartCommandResult.Sticky
                    : StartCommandResult.NotSticky;

            case "disconnect":
                _vpnServiceHost.Disconnect();
                return StartCommandResult.NotSticky;

            default:
                return StartCommandResult.NotSticky;
        }
    }

    public IVpnAdapter CreateAdapter()
    {
        return new AndroidVpnAdapter(this);
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
        StopForeground(StopForegroundFlags.Remove);

        // clear notification
        _notification?.Dispose();
        _notification = null;
    }

    public override void OnDestroy()
    {
        VhLogger.Instance.LogDebug("VpnService is destroying.");
        _ = _vpnServiceHost.DisposeAsync();

        StopNotification();
        base.OnDestroy();
    }
}
