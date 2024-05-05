using Android;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device.Droid.Utils;
using Permission = Android.Content.PM.Permission;

namespace VpnHood.Client.App.Droid.Common
{
    public class AndroidAppUiService : IAppUiService
    {
        private const int RequestPostNotificationId = 11;
        private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;

        public bool IsQuickLaunchSupported => OperatingSystem.IsAndroidVersionAtLeast(33);
        public async Task<bool> RequestQuickLaunch(IAppUiContext context, CancellationToken cancellationToken)
        {
            var androidAppUiContext = (AndroidAppUiContext)context;

            if (!IsQuickLaunchSupported)
                throw new NotSupportedException("QuickLaunch is not supported on this device.");

            // request for adding tile
            // result. 0: reject, 1: already granted, 2: granted 
            var res = await QuickLaunchTileService.RequestAddTile(androidAppUiContext.Activity)
                .WaitAsync(cancellationToken);
            return res != 0;
        }

        public bool IsNotificationSupported => OperatingSystem.IsAndroidVersionAtLeast(33);
        public async Task<bool> RequestNotification(IAppUiContext context, CancellationToken cancellationToken)
        {
            var androidAppUiContext = (AndroidAppUiContext)context;

            // check is request supported
            if (!IsNotificationSupported)
                throw new NotSupportedException("RequestNotification is not supported on this device.");

            // check is already granted
            if (androidAppUiContext.Activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted)
                return true;

            try
            {
                androidAppUiContext.ActivityEvent.RequestPermissionsResultEvent += OnRequestPermissionsResult;

                // request for notification
                _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
                androidAppUiContext.Activity.RequestPermissions([Manifest.Permission.PostNotifications], RequestPostNotificationId);
                var res = await _requestPostNotificationsCompletionTask.Task.WaitAsync(cancellationToken);
                return res == Permission.Granted;
            }
            finally
            {
                androidAppUiContext.ActivityEvent.RequestPermissionsResultEvent -= OnRequestPermissionsResult;
            }
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
}