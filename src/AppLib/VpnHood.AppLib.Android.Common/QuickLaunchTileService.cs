using Android;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Service.QuickSettings;
using Java.Util.Functions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.Droid.Utils;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Droid.Common;

// A passive tile running in its own lightweight process. It never touches VpnHoodApp: state and
// disconnect go through VpnServiceManager, which reads the vpn.status file first and only talks
// to the service over the message channel when the file says it is running (a dormant binding
// that cannot start the service), so querying while the service is stopped simply reads as not
// connected. A tap either disconnects, starts the VPN service with its last configuration (the
// always-on bootstrap), or — when no configuration exists — opens the main app.
//
// Deliberately PASSIVE (no MetaDataActiveTile): a passive tile gets a listening window on every
// Quick Settings open, so it re-queries and self-heals there — even after the VPN process was
// killed without a trace. An active tile paints only what was last pushed: the VPN service would
// have to nudge it via RequestListeningState on every state change (cross-layer discovery plus
// debouncing, and each nudge spawns this process with nobody watching), yet after a crash no
// final nudge ever comes and the tile would show Connected until tapped. The passive cost — one
// tile-process spawn per panel open — is acceptable and shrinks further with AOT.
[Service(
#if !DEBUG
    Process = ProcessName,
#endif
    Permission = Manifest.Permission.BindQuickSettingsTile, Icon = IconResourceName, Enabled = true,
    Exported = true)]
[MetaData(MetaDataToggleableTile, Value = "true")]
[IntentFilter([ActionQsTile])]
public class QuickLaunchTileService : TileService
{
    public const string ProcessName = ":vpnhood_tile";
    private const string IconResourceName = "@mipmap/quick_launch_tile";
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private readonly VpnServiceManager _vpnServiceManager = new(AndroidDevice.Create(), eventWatcherInterval: null);
    private CancellationTokenSource? _listeningCts;
    private ClientState? _overrideClientState; // for immediate visual feedback while the service is starting/stopping
    public static bool IsTileProcess => AndroidDevice.CurrentProcessName.Contains(ProcessName);

    public override void OnCreate()
    {
        base.OnCreate();
        VhLogger.Instance.LogDebug("QuickLaunchTileService has been created.");
    }

    public override void OnDestroy()
    {
        _listeningCts?.TryCancel();
        _listeningCts?.TryDispose();
        _listeningCts = null;
        _vpnServiceManager.Dispose(); // does not stop the service
        _mainHandler.RemoveCallbacksAndMessages(null);
        _mainHandler.Dispose();
        base.OnDestroy();
    }

    public override void OnTileAdded()
    {
        VhLogger.Instance.LogDebug("OnTileAdded is requested.");
        base.OnTileAdded();
    }

    public override void OnTileRemoved()
    {
        VhLogger.Instance.LogDebug("OnTileRemoved is requested.");
        base.OnTileRemoved();
    }

    public override void OnStartListening()
    {
        base.OnStartListening();
        _listeningCts?.TryCancel();
        _listeningCts?.TryDispose();
        _listeningCts = new CancellationTokenSource();
        _ = PollWhileListening(_listeningCts.Token);
    }

    public override void OnStopListening()
    {
        _listeningCts?.TryCancel();
        base.OnStopListening();
    }

    // keep the tile live while the Quick Settings panel is open; the tile process is tiny and
    // stops polling the moment the panel closes
    private async Task PollWhileListening(CancellationToken cancellationToken)
    {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                RefreshTile();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
            }
        }
        catch (System.OperationCanceledException) {
            // the listening window is over
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not refresh the Quick Launch tile.");
        }
    }

    private void RefreshTile()
    {
        if (!ReferenceEquals(Looper.MyLooper(), Looper.MainLooper)) {
            _mainHandler.Post(RefreshTileCore);
            return;
        }

        RefreshTileCore();
    }

    private void RefreshTileCore()
    {
        var qsTile = QsTile;
        if (qsTile == null)
            return;

        var connectionInfo = _vpnServiceManager.ConnectionInfo;

        // override the client state while the service is stopping to effect immediate visual feedback;
        // otherwise report the real state so a dead or reconnecting session never reads as Connected
        var clientState = _overrideClientState ?? connectionInfo.ClientState;
        if (_vpnServiceManager.IsStopping)
            clientState = ClientState.Disconnecting;
        var qsStateDescription = clientState.ToString();
        var qsLabel = connectionInfo.SessionName ?? AndroidUtils.GetAppName(Application.Context);
        var qsState = clientState is ClientState.None or ClientState.Disposed or ClientState.Disconnecting
            ? TileState.Inactive : TileState.Active;
        UpdateTile(qsTile, qsState, qsLabel, qsStateDescription);
    }


    private static void UpdateTile(Tile tile, TileState state, string? label, string? stateDescription)
    {
        // avoid unnecessary updates that would trigger a tile animation
        if (tile.State == state && tile.Label == label &&
            (!OperatingSystem.IsAndroidVersionAtLeast(30) || tile.StateDescription == stateDescription)) {
            return;
        }

        tile.State = state;
        tile.Label = label;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            tile.StateDescription = stateDescription;
        tile.UpdateTile();
    }

    public override void OnClick()
    {
        _ = TryClick();
    }


    private async Task TryClick()
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try {
            if (!_vpnServiceManager.IsConfigured) {
                // no configuration yet; only the main app can create one
                LaunchMainApp();
                return;
            }

            if (_vpnServiceManager.IsStarted) {
                _overrideClientState = ClientState.Disconnecting;
                RefreshTile();
                await _vpnServiceManager.TryStop().Vhc();
                return;
            }

            // start the VPN service with its last configuration — the same bootstrap always-on uses.
            // Starting a foreground service from a tile tap is within the FGS exemptions
            _overrideClientState = ClientState.Initializing;
            RefreshTile();
            await _vpnServiceManager.StartFromLastConfig(timeoutCts.Token).Vhc();
        }
        catch (Exception ex) {
            AndroidUtils.ShowToast(ex.Message);
        }
        finally {
            _overrideClientState = null;
        }
    }

    private void LaunchMainApp()
    {
        var appName = AndroidUtils.GetAppName(Application.Context);
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        if (launchIntent == null) {
            AndroidUtils.ShowToast($"VPN is not configured yet. Please open {appName} first.");
            return;
        }

        try {
            launchIntent.AddFlags(ActivityFlags.NewTask);
            if (OperatingSystem.IsAndroidVersionAtLeast(34)) {
                var pendingIntent = PendingIntent.GetActivity(this, requestCode: 0, launchIntent,
                    PendingIntentFlags.Immutable);
                if (pendingIntent != null)
                    StartActivityAndCollapse(pendingIntent);
            }
            else {
                StartActivityAndCollapse(launchIntent);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not launch the main app from the tile.");
            AndroidUtils.ShowToast($"VPN is not configured yet. Please open {appName} first.");
        }
    }

    private class AddTileServiceHandler(TaskCompletionSource<int> taskCompletionSource)
        : Java.Lang.Object, IConsumer
    {
        public void Accept(Java.Lang.Object? obj)
        {
            obj ??= 0;
            taskCompletionSource.TrySetResult((int)obj);
        }
    }

    public static Task<int> RequestAddTile(Context context)
    {
        // the user is being prompted right now; do not prompt again later. This runs in the app
        // process — the tile process never touches app settings
        if (VpnHoodApp.IsInit && !VpnHoodApp.Instance.Settings.UserSettings.IsQuickLaunchPrompted) {
            VpnHoodApp.Instance.Settings.UserSettings.IsQuickLaunchPrompted = true;
            VpnHoodApp.Instance.Settings.Save();
        }

        // get statusBarManager
        if (context.GetSystemService(StatusBarService) is not StatusBarManager statusBarManager) {
            VhLogger.Instance.LogError("Could not retrieve the StatusBarManager.");
            return Task.FromResult(0);
        }

        if (context.MainExecutor == null) {
            VhLogger.Instance.LogError("Could not retrieve the MainExecutor.");
            return Task.FromResult(0);
        }

        VhLogger.Instance.LogDebug("Creating Tile...");
        ArgumentNullException.ThrowIfNull(context.PackageManager);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.Resources);
        var appName = context.PackageManager.GetApplicationLabel(
            context.PackageManager.GetApplicationInfo(context.PackageName, PackageInfoFlags.MetaData));
        var iconId = context.Resources.GetIdentifier(IconResourceName, "drawable", context.PackageName);
        var icon = Icon.CreateWithResource(context, iconId);

        VhLogger.Instance.LogDebug("Calling RequestAddTileService API...");
        var taskCompletionSource = new TaskCompletionSource<int>();
        statusBarManager.RequestAddTileService(
            new ComponentName(context, Java.Lang.Class.FromType(typeof(QuickLaunchTileService))),
            appName, icon,
            context.MainExecutor!,
            new AddTileServiceHandler(taskCompletionSource));

        return taskCompletionSource.Task;
    }
}
