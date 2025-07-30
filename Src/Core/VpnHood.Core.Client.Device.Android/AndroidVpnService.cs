using System.Diagnostics;
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
//#if !DEBUG  
    Process = ProcessName,
//#endif
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService, IVpnServiceHandler
{
    private VpnServiceHost? _vpnServiceHost;
    private AndroidVpnNotification? _notification;
    public const string ProcessName = ":vpnhood_process";

    public static string VpnServiceConfigFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");

    public AndroidVpnService()
    {
        VhLogger.Instance.LogInformation(
            "AndroidVpnService has bee constructed. ProcessId: {ProcessId}", Process.GetCurrentProcess().Id);
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        VhLogger.Instance.LogInformation(
            "AndroidVpnService OnStartCommand. Action: {Action}, ProcessId: {ProcessId}",
            action, Process.GetCurrentProcess().Id);

        // get "manual" in 
        switch (action) {
            // signal start command
            case null or "android.net.VpnService" or "connect":
                return ProcessConnectAction(action == "connect");

            case "disconnect":
                return ProcessDisconnectAction();

            default:
                return StartCommandResult.NotSticky;
        }
    }

    private StartCommandResult ProcessConnectAction(bool forceReconnect)
    {
        // Create StartForeground and show notification as soon as possible
        if (_notification is null)
            ShowNotification(VpnServiceHost.DefaultConnectionInfo);

        // start the VPN service host and connect to the VPN
        Task.Run(async () => {
            try {
                VhLogger.Instance.LogDebug("Starting VPN service host.");
                _vpnServiceHost ??= new VpnServiceHost(VpnServiceConfigFolder, this, new SocketFactory());
                if (!await _vpnServiceHost.TryConnect(forceReconnect: forceReconnect))
                    StopSelf();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not start VPN service host.");
                StopSelf();
            }
        });

        return StartCommandResult.Sticky;
    }

    private StartCommandResult ProcessDisconnectAction()
    {
        if (_vpnServiceHost != null)
            _ = _vpnServiceHost.TryDisconnect();
        else
            StopSelf();
        return StartCommandResult.NotSticky;
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
            VhLogger.Instance.LogDebug("Create and show the notification for the VPN service.");
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

        _vpnServiceHost?.Dispose();
        _vpnServiceHost = null;
        base.OnDestroy();
    }
}
