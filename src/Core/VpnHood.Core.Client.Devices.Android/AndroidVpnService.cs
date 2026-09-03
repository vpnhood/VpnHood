using System.Diagnostics;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Droid.Messaging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Quic.Droid;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.AndroidTun;

namespace VpnHood.Core.Client.Devices.Droid;

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
    private readonly AndroidMessageListener _messageListener = new();
    public const string ProcessName = ":vpnhood_process";
    public const string ActionConnect = "connect";
    public const string ActionDisconnect = "disconnect";

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
        VhLogger.Instance.LogInformation( // logger may not be initialized yet
            "AndroidVpnService OnStartCommand. Action: {Action}, ProcessId: {ProcessId}",
            action, Process.GetCurrentProcess().Id);

        // Promote to the foreground before anything else; it is mandatory after startForegroundService.
        // A refused promotion (e.g. ForegroundServiceStartNotAllowedException) must end the start here:
        // continuing would let the system kill the process ten seconds later with
        // ForegroundServiceDidNotStartInTimeException, and returning Sticky would restart it into the
        // same failure.
        if (!TryShowNotification(VpnServiceHost.DefaultConnectionInfo)) {
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        // get "manual" in
        return action switch {
            // signal start command
            null or "android.net.VpnService" => ProcessConnectAction(forceReconnect: false, alwaysOn: true),
            ActionConnect => ProcessConnectAction(forceReconnect: true, alwaysOn: false),
            ActionDisconnect => ProcessDisconnectAction(),
            _ => ProcessUnknownAction(action)
        };
    }

    // the message listener claims only its own bind action; everything else (especially the
    // system's android.net.VpnService bind that establishes the VPN) goes to the base VpnService
    public override Android.OS.IBinder? OnBind(Intent? intent)
    {
        return _messageListener.TryBind(intent) ?? base.OnBind(intent);
    }

    private StartCommandResult ProcessConnectAction(bool forceReconnect, bool alwaysOn)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            alwaysOn |= IsAlwaysOn;

        // start the VPN service host and connect to the VPN
        Task.Run(async () => {
            try {
                VhLogger.Instance.LogDebug("Starting VPN service host. AlwaysOn: {AlwaysOn}", alwaysOn);
                _vpnServiceHost ??= new VpnServiceHost(
                    configFolder: VpnServiceConfigFolder,
                    vpnServiceHandler: this,
                    socketFactory: new AndroidSocketFactory(),
                    messageListener: _messageListener);

                if (!await _vpnServiceHost.TryConnect(forceReconnect: forceReconnect, isAlwaysOn: alwaysOn))
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


    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
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
        TryShowNotification(connectionInfo);
    }

    // Promotes the service to the foreground on first use, then keeps the notification in step with
    // the connection state. Returns false when the system refused the promotion.
    private bool TryShowNotification(ConnectionInfo connectionInfo)
    {
        var notification = _notification ?? TryStartForeground(connectionInfo.SessionName ?? "VPN");
        if (notification == null)
            return false;

        notification.Update(connectionInfo.ClientState);
        return true;
    }

    // Builds the notification and promotes the service with it. _notification is assigned only once
    // the system has accepted the promotion, so a failed attempt (the builder, Build() and
    // StartForeground can all throw) leaves nothing behind and the next start command retries.
    // Reports the failure instead of throwing: a throw from OnStartCommand leaves no returned mode
    // and Android would restart the service into a back-off crash loop.
    private AndroidVpnNotification? TryStartForeground(string sessionName)
    {
        VhLogger.Instance.LogDebug("Create and show the notification for the VPN service.");
        try {
            var notification = new AndroidVpnNotification(this, new VpnServiceLocalization(), sessionName);
            StartForeground(AndroidVpnNotification.NotificationId, notification.Build());
            _notification = notification;
            return notification;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not promote the VPN service to the foreground.");
            return null;
        }
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
            _ = _vpnServiceHost.TryDisconnect(new VpnServiceRevokedException());
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

        // the host's ApiController disposes the listener; this covers the bind-only case
        _messageListener.Dispose();

        base.OnDestroy();
    }
}