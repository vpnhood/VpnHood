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

// VPN requires TypeSystemExempted:  https://developer.android.com/about/versions/14/changes/fgs-types-required#system-exempted
[Service(
#if !DEBUG
    Process = ProcessName,
#endif
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeSystemExempted
    )]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnService : VpnService, IVpnServiceHandler
{
    private VpnServiceHost? _vpnServiceHost;
    private AndroidVpnNotification? _notification;
    public const string ProcessName = ":vpnhood_process";

    public static string VpnServiceConfigFolder =>
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

        // Create StartForeground and show notification as soon as possible. It is mandatory
        if (_notification is null)
            ShowNotification(VpnServiceHost.DefaultConnectionInfo);

        // get "manual" in 
        return action switch {
            // signal start command
            null or "android.net.VpnService" or "connect" => ProcessConnectAction(action == "connect"),
            "disconnect" => ProcessDisconnectAction(),
            _ => ProcessUnknownAction(action)
        };
    }

    private StartCommandResult ProcessConnectAction(bool forceReconnect)
    {
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

    private StartCommandResult ProcessUnknownAction(string action)
    {
        VhLogger.Instance.LogWarning("VpnService received an unknown action: {Action}", action);
        if (_vpnServiceHost != null)
            return StartCommandResult.Sticky;

        StopSelf(); // unknow command
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

    public override void OnRevoke()
    {
        if (_vpnServiceHost != null) {
            VhLogger.Instance.LogDebug("VpnService is revoked, disconnecting.");
            _ = _vpnServiceHost.TryDisconnect();
            return;
        }

        base.OnRevoke();
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
