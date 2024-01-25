using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android;
using Android.Net;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common.Activities;

public abstract class AndroidAppMainActivity : Activity
{
    private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;
    protected const int RequestVpnPermissionId = 10;
    protected const int RequestPostNotificationId = 11;
    protected AndroidDevice VpnDevice => AndroidDevice.Current ?? throw new InvalidOperationException($"{nameof(AndroidDevice)} has not been initialized.");
    protected string[] AccessKeySchemes { get; set; } = Array.Empty<string>();
    protected string[] AccessKeyMimes { get; set; } = Array.Empty<string>();
    protected abstract IAppUpdaterService? CreateAppUpdaterService();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // manage VpnPermission
        VpnDevice.OnRequestVpnPermission += Device_OnRequestVpnPermission;

        // process intent
        ProcessIntent(Intent);
    }

    protected async Task RequestFeatures()
    {
        // request for adding tile
        if (!VpnHoodApp.Instance.Settings.IsQuickLaunchRequested &&
            OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            VpnHoodApp.Instance.Settings.IsQuickLaunchRequested = true;
            VpnHoodApp.Instance.Settings.Save();
            await QuickLaunchTileService.RequestAddTile(this);
        }

        // request for notification
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
            RequestPermissions([Manifest.Permission.PostNotifications], RequestPostNotificationId);
            await _requestPostNotificationsCompletionTask.Task;
        }

        // Check for update
        var appUpdaterService = CreateAppUpdaterService();
        VpnHoodApp.Instance.AppUpdaterService = appUpdaterService;
        if (VpnHoodApp.Instance.VersionCheckRequired && appUpdaterService != null && await appUpdaterService.Update())
            VpnHoodApp.Instance.VersionCheckPostpone(); // postpone check if check succeeded
    }

    protected override void OnNewIntent(Intent? intent)
    {
        if (!ProcessIntent(intent))
            base.OnNewIntent(intent);
    }

    private bool ProcessIntent(Intent? intent)
    {
        if (intent?.Data == null || ContentResolver == null)
            return false;

        // try to add the access key
        try
        {
            var uri = intent.Data;
            if (AccessKeySchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                ImportAccessKey(uri.ToString()!);
                return true;
            }

            // check mime
            var mimeType = ContentResolver.GetType(uri);
            if (!AccessKeyMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            {
                Toast.MakeText(this, VpnHoodApp.Instance.Resources.Strings.MsgUnsupportedContent, ToastLength.Long)?.Show();
                return false;
            }

            // open stream
            using var inputStream = ContentResolver.OpenInputStream(uri);
            if (inputStream == null)
                throw new Exception("Can not open the intent file stream.");

            // read string into buffer maximum 5k
            var buffer = new byte[5 * 1024];
            var length = inputStream.Read(buffer);
            using var memoryStream = new MemoryStream(buffer, 0, length);
            var streamReader = new StreamReader(memoryStream);
            var accessKey = streamReader.ReadToEnd();

            ImportAccessKey(accessKey);
        }
        catch
        {
            Toast.MakeText(this, VpnHoodApp.Instance.Resources.Strings.MsgCantReadAccessKey, ToastLength.Long)?.Show();
        }

        return true;
    }

    protected void ImportAccessKey(string accessKey)
    {
        var profiles = VpnHoodApp.Instance.ClientProfileService.List();
        var profile = VpnHoodApp.Instance.ClientProfileService.ImportAccessKey(accessKey).ToInfo();
        _ = VpnHoodApp.Instance.Disconnect(true);
        VpnHoodApp.Instance.UserSettings.DefaultClientProfileId = profile.ClientProfileId;

        var isNew = profiles.Any(x => x.ClientProfileId == profile.ClientProfileId);
        var message = isNew
            ? string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyAdded, profile.ClientProfileName)
            : string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyUpdated, profile.ClientProfileName);

        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }

    private void Device_OnRequestVpnPermission(object? sender, EventArgs e)
    {
        var intent = VpnService.Prepare(this);
        if (intent == null)
            VpnDevice.VpnPermissionGranted();
        else
            StartActivityForResult(intent, RequestVpnPermissionId);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        var postNotificationsIndex = Array.IndexOf(permissions, Manifest.Permission.PostNotifications);
        if (postNotificationsIndex != -1 && _requestPostNotificationsCompletionTask != null)
            _requestPostNotificationsCompletionTask.TrySetResult(grantResults[postNotificationsIndex]);

        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        if (requestCode == RequestVpnPermissionId)
        {
            if (resultCode == Result.Ok)
                VpnDevice.VpnPermissionGranted();
            else
                VpnDevice.VpnPermissionRejected();
        }
    }

    protected override void OnDestroy()
    {
        VpnDevice.OnRequestVpnPermission -= Device_OnRequestVpnPermission;
        VpnHoodApp.Instance.AppUpdaterService = null;

        base.OnDestroy();
    }
}