using Android;
using Android.Content;
using Android.Net;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Exceptions;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using Permission = Android.Content.PM.Permission;

namespace VpnHood.AppLib.Droid.Common;

public class AndroidDeviceUiProvider : IDeviceUiProvider
{
    private const int RequestPostNotificationId = 11;
    private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;
    private static bool IsTv => AndroidUtil.IsTv();

    public bool IsQuickLaunchSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(24) && !IsTv;

    public bool IsRequestQuickLaunchSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(33) && !IsTv;

    public bool IsPrivateDnsSettingsSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(28) && !IsTv;

    public bool IsKillSwitchSettingsSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(24) && !IsTv;

    public bool IsAlwaysOnSettingsSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(24) && !IsTv;

    public bool IsRequestNotificationSupported =>
        OperatingSystem.IsAndroidVersionAtLeast(33) && !IsTv;

    public bool IsAppNotificationSettingsSupported => !IsTv;
    public bool IsSettingsSupported => !IsTv;
    public bool IsAppSettingsSupported => !IsTv;

    public async Task<bool> RequestQuickLaunch(IUiContext context, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)context;

        if (!IsRequestQuickLaunchSupported)
            throw new NotSupportedException("QuickLaunch is not supported on this device.");

        // request for adding tile
        // result. 0: reject, 1: already granted, 2: granted 
        var resuestTime = DateTime.Now;
        var res = await QuickLaunchTileService
            .RequestAddTile(appUiContext.Activity)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (res <= 0 && DateTime.Now - resuestTime < TimeSpan.FromMilliseconds(1000))
            throw new RequestQuickLaunchException(
                "Unable to add the Quick Launch. Try again later or add it manually.");

        return res > 0;
    }

    public bool? IsNotificationEnabled {
        get {
            var notificationManager =
                (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
            return notificationManager?.AreNotificationsEnabled();
        }
    }

    public async Task<bool> RequestNotification(IUiContext context, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)context;

        // check is request supported
        if (!IsRequestNotificationSupported)
            throw new NotSupportedException("RequestNotification is not supported on this device.");

        // check is already granted
        if (appUiContext.Activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted)
            return true;

        try {
            appUiContext.ActivityEvent.RequestPermissionsResultEvent += OnRequestPermissionsResult;

            // request for notification
            _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
            appUiContext.Activity.RequestPermissions([Manifest.Permission.PostNotifications],
                RequestPostNotificationId);
            var res = await _requestPostNotificationsCompletionTask.Task
                .WaitAsync(cancellationToken)
                .Vhc();

            return res == Permission.Granted;
        }
        finally {
            appUiContext.ActivityEvent.RequestPermissionsResultEvent -= OnRequestPermissionsResult;
        }
    }

    public void OpenKillSwitchSettings(IUiContext context)
    {
        if (!IsKillSwitchSettingsSupported)
            throw new NotSupportedException("SystemKillSwitchSettings is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        var intent = new Intent(Android.Provider.Settings.ActionVpnSettings);
        appUiContext.Activity.StartActivity(intent);
    }

    public void OpenAlwaysOnSettings(IUiContext context)
    {
        if (!IsAlwaysOnSettingsSupported)
            throw new NotSupportedException("SystemAlwaysOnSettings is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        var intent = new Intent(Android.Provider.Settings.ActionVpnSettings);
        appUiContext.Activity.StartActivity(intent);
    }

    public void OpenSettings(IUiContext context)
    {
        if (!IsSettingsSupported)
            throw new NotSupportedException("SystemSettings is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        var intent = new Intent(Android.Provider.Settings.ActionSettings);
        appUiContext.Activity.StartActivity(intent);
    }

    public void OpenAppSettings(IUiContext context)
    {
        if (!IsAppSettingsSupported)
            throw new NotSupportedException("AppSystemSettings is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
        intent.SetData(Android.Net.Uri.FromParts("package", appUiContext.Activity.PackageName, null));
        appUiContext.Activity.StartActivity(intent);
    }

    public void OpenAppNotificationSettings(IUiContext context)
    {
        if (!IsAppNotificationSettingsSupported)
            throw new NotSupportedException("AppSystemNotificationSettings is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) {
            var intent = new Intent(Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, appUiContext.Activity.PackageName);
            appUiContext.Activity.StartActivity(intent);
        }
        else {
            var intent = new Intent(Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra("app_package", appUiContext.Activity.PackageName);
            intent.PutExtra("app_uid", appUiContext.Activity.ApplicationInfo!.Uid);
            appUiContext.Activity.StartActivity(intent);
        }
    }

    public bool IsProxySettingsSupported => OperatingSystem.IsAndroidVersionAtLeast(21);

    public DeviceProxySettings? GetProxySettings()
    {
        if (!IsProxySettingsSupported)
            return null;

        try {
            var connectivityManager =
                (ConnectivityManager?)Application.Context.GetSystemService(Context.ConnectivityService) ??
                throw new Exception("Could not retrieve ConnectivityManager for proxy settings.");

            // Get active network
            var network =
                connectivityManager.ActiveNetwork ??
                throw new Exception("Could not retrieve active network for proxy settings.");

            var linkProperties =
                connectivityManager.GetLinkProperties(network) ??
                throw new Exception("Could not retrieve LinkProperties for proxy settings.");

            var httpProxy = linkProperties.HttpProxy;
            if (httpProxy?.Host is null) {
                VhLogger.Instance.LogDebug("No HTTP proxy configured.");
                return null;
            }

            // Get exclusion list
            var exclusionList = httpProxy.GetExclusionList()?.ToArray() ?? [];
            return new DeviceProxySettings {
                ProxyUrl = new UriBuilder(
                    scheme: "http://", host: httpProxy.Host, portNumber: httpProxy.Port).Uri,
                PacFileUrl = httpProxy.PacFileUrl?.ToString()?.Trim(),
                ExcludeDomains = exclusionList
            };
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error retrieving proxy settings.");
            return null;
        }
    }

    public PrivateDns? GetPrivateDns()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) // < 28
            return null;

        var connectivityManager = (ConnectivityManager?)Application.Context
            .GetSystemService(Context.ConnectivityService);

        // get active network
        var network = connectivityManager?.ActiveNetwork;
        if (connectivityManager is null) {
            VhLogger.Instance.LogDebug("Could not retrieve active network for PrivateDns.");
            return null;
        }

        var linkProperties = connectivityManager.GetLinkProperties(network);
        if (linkProperties is null) {
            VhLogger.Instance.LogDebug("Could not retrieve LinkProperties for PrivateDns.");
            return null;
        }

        // Note: Provider is non-null only in strict "hostname" mode.
        return new PrivateDns {
            IsActive = linkProperties.IsPrivateDnsActive,
            Provider = linkProperties.PrivateDnsServerName
        };
    }

    public SystemBarsInfo GetBarsInfo(IUiContext uiContext)
    {
        // check is request supported for WindowInsets.Type.SystemBars()
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return SystemBarsInfo.Default;

        // get system bars info
        var appUiContext = (AndroidUiContext)uiContext;
        var contentRoot = appUiContext.Activity.Window?.DecorView;
        var rect = contentRoot?.RootWindowInsets?.GetInsets(WindowInsets.Type.SystemBars());

        return rect == null
            ? SystemBarsInfo.Default
            : new SystemBarsInfo {
                TopHeight = rect.Top,
                BottomHeight = rect.Bottom
            };
    }

    private void OnRequestPermissionsResult(object? sender, RequestPermissionsResultArgs e)
    {
        if (e.RequestCode != RequestPostNotificationId)
            return;

        var postNotificationsIndex = Array.IndexOf(e.Permissions, Manifest.Permission.PostNotifications);
        if (postNotificationsIndex != -1 && _requestPostNotificationsCompletionTask != null)
            _requestPostNotificationsCompletionTask.TrySetResult(e.GrantResults[postNotificationsIndex]);
    }
}