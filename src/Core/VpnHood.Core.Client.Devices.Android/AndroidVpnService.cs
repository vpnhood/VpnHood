using System.Diagnostics;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Android.Messaging;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.Android;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.AndroidTun;

namespace VpnHood.Core.Client.Devices.Android;

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
    private readonly Lock _notificationLock = new();
    private readonly AndroidMessageListener _messageListener = new();
    private volatile int _lastStartId = -1; // -1: no start yet, and a stop then stops all
    public const string ProcessName = ":vpnhood_process";
    public const string ActionConnect = "connect";
    public const string ActionDisconnect = "disconnect";

    // the service's own files: Android never puts the no-backup folder in a backup or a transfer
    public static string VpnServiceConfigFolder =>
        Path.Combine(Application.Context.NoBackupFilesDir?.AbsolutePath ??
                     throw new InvalidOperationException("Android named no no-backup folder for the app."), "vpn-service");

    // the person's proxies, beside the app's files, which travel with a backup
    public static string VpnServiceDataFolder =>
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
        if (!TryShowNotification(VpnServiceHost.DefaultConnectionInfo, CancellationToken.None)) {
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        // Every stop from here on is of this start, once promoted. Android counts a start as it is asked
        // for, before this method hears of it, and stopping the service while it waits for that start's
        // promotion crashes the app (ForegroundServiceDidNotStartInTime): a connect right after a
        // disconnect or a failed connect met the host's stop of the one before.
        _lastStartId = startId;

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
    public override global::Android.OS.IBinder? OnBind(Intent? intent)
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
                // a log file of its own only in its own process; in the app's (a Debug build) its lines
                // go to the app's file
                _vpnServiceHost ??= new VpnServiceHost(
                    configFolder: VpnServiceConfigFolder,
                    dataFolder: VpnServiceDataFolder,
                    vpnServiceHandler: this,
                    socketFactory: new AndroidSocketFactory(),
                    messageListener: _messageListener,
                    withLogger: AndroidDevice.IsVpnServiceProcess);

                if (!await _vpnServiceHost.TryConnect(forceReconnect: forceReconnect, isAlwaysOn: alwaysOn).Vhc())
                    StopSelf(_lastStartId);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not start VPN service host.");
                StopSelf(_lastStartId);
            }
        });

        return StartCommandResult.Sticky;
    }

    private StartCommandResult ProcessDisconnectAction()
    {
        if (_vpnServiceHost != null)
            _ = _vpnServiceHost.TryDisconnect();
        else
            StopSelf(_lastStartId);
        return StartCommandResult.NotSticky;
    }

    private StartCommandResult ProcessUnknownAction(string action)
    {
        VhLogger.Instance.LogWarning("VpnService received an unknown action: {Action}", action);
        if (_vpnServiceHost != null)
            return StartCommandResult.Sticky;

        StopSelf(_lastStartId); // unknow command
        return StartCommandResult.NotSticky;
    }

    // the host's stop, of the last start this service has promoted: a start Android has taken since keeps it
    void IVpnServiceHandler.StopSelf()
    {
        StopSelf(_lastStartId);
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

    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        TryShowNotification(connectionInfo, cancellationToken);
    }

    // Promotes the service to the foreground on first use, then keeps the notification in step with
    // the connection state. Returns false when the system refused the promotion. One at a time with
    // StopNotification: start commands come on the main thread and the host's calls on its own, and a
    // notification stopped in between would be updated after its disposal.
    private bool TryShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        lock (_notificationLock) {
            var notification = _notification ?? TryStartForeground(cancellationToken);
            if (notification == null)
                return false;

            notification.Update(connectionInfo.ClientState);
            return true;
        }
    }

    // Builds the notification and promotes the service with it. _notification is assigned only once
    // the system has accepted the promotion, so a failed attempt (the builder, Build() and
    // StartForeground can all throw) leaves nothing behind and the next start command retries.
    // Reports the failure instead of throwing: a throw from OnStartCommand leaves no returned mode
    // and Android would restart the service into a back-off crash loop. A cancel goes on: only the
    // host's calls bring a token that can be cancelled.
    private AndroidVpnNotification? TryStartForeground(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Create and show the notification for the VPN service.");
        try {
            // The words and the session's name of the app's last connect, which its options file keeps,
            // as the notification comes before the host reads them: English before any connect, and the
            // app's name where the app names no session. Blocked on: OnStartCommand must have the promotion's
            // answer before it returns.
            var serviceOptions = new VpnServiceOptionsFile(VpnServiceConfigFolder).TryRead(cancellationToken)
                .GetAwaiter().GetResult();
            var notification = new AndroidVpnNotification(this,
                serviceOptions?.Localization ?? new VpnServiceLocalization(),
                serviceOptions?.ClientOptions.SessionName ?? AndroidUtils.GetAppName(this));
            StartForeground(AndroidVpnNotification.NotificationId, notification.Build());
            _notification = notification;
            return notification;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not promote the VPN service to the foreground.");
            return null;
        }
    }

    public void StopNotification()
    {
        lock (_notificationLock) {
            if (_notification == null)
                return;

            VhLogger.Instance.LogDebug("Remove VpnService from foreground and stop the notification.");
            StopForeground(StopForegroundFlags.Remove);

            // clear notification
            _notification.Dispose();
            _notification = null;
        }
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