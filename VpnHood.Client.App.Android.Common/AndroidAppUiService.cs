using Android;
using Android.Content;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Device.Droid.ActivityEvents;
using VpnHood.Common.Utils;
using Permission = Android.Content.PM.Permission;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidAppUiService : IAppUiService
{
    private const int RequestPostNotificationId = 11;
    private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;

    public bool IsQuickLaunchSupported => OperatingSystem.IsAndroidVersionAtLeast(33);
    public async Task<bool> RequestQuickLaunch(IUiContext context, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)context;

        if (!IsQuickLaunchSupported)
            throw new NotSupportedException("QuickLaunch is not supported on this device.");

        // request for adding tile
        // result. 0: reject, 1: already granted, 2: granted 
        var res = await QuickLaunchTileService
            .RequestAddTile(appUiContext.Activity)
            .WaitAsync(cancellationToken)
            .VhConfigureAwait();
        return res != 0;
    }

    public bool IsNotificationSupported => OperatingSystem.IsAndroidVersionAtLeast(33);
    public async Task<bool> RequestNotification(IUiContext context, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)context;

        // check is request supported
        if (!IsNotificationSupported)
            throw new NotSupportedException("RequestNotification is not supported on this device.");

        // check is already granted
        if (appUiContext.Activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted)
            return true;

        try
        {
            appUiContext.ActivityEvent.RequestPermissionsResultEvent += OnRequestPermissionsResult;

            // request for notification
            _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
            appUiContext.Activity.RequestPermissions([Manifest.Permission.PostNotifications], RequestPostNotificationId);
            var res = await _requestPostNotificationsCompletionTask.Task
                .WaitAsync(cancellationToken)
                .VhConfigureAwait();
            return res == Permission.Granted;
        }
        finally
        {
            appUiContext.ActivityEvent.RequestPermissionsResultEvent -= OnRequestPermissionsResult;
        }
    }

    public bool IsOpenAlwaysOnPageSupported => OperatingSystem.IsAndroidVersionAtLeast(24);
    public void OpenAlwaysOnPage(IUiContext context)
    {
        if (!IsOpenAlwaysOnPageSupported)
            throw new NotSupportedException("AlwaysOn is not supported on this device.");

        var appUiContext = (AndroidUiContext)context;
        var intent = new Intent(Android.Provider.Settings.ActionVpnSettings);
        appUiContext.Activity.StartActivity(intent);
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