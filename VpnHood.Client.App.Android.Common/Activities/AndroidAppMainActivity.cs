using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android;
using VpnHood.Client.Device.Droid;
using Android.Net;

namespace VpnHood.Client.App.Droid.Common.Activities;

public abstract class AndroidAppMainActivity : Activity
{
    protected const int RequestVpnPermissionId = 10;
    protected const int RequestPostNotificationId = 11;
    protected AndroidDevice VpnDevice => AndroidDevice.Current ?? throw new InvalidOperationException($"{nameof(AndroidDevice)} has not been initialized.");
    protected string[] AccessKeySchemes { get; set; } = Array.Empty<string>();
    protected string[] AccessKeyMimes { get; set; } = Array.Empty<string>();

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
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, RequestPostNotificationId);
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
        var accessKeyStatus = VpnHoodApp.Instance.ClientProfileStore.GetAccessKeyStatus(accessKey);
        var profile = VpnHoodApp.Instance.ClientProfileStore.AddAccessKey(accessKey);
        _ = VpnHoodApp.Instance.Disconnect(true);
        VpnHoodApp.Instance.UserSettings.DefaultClientProfileId = profile.ClientProfileId;

        var message = accessKeyStatus.ClientProfile != null
            ? string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyUpdated, accessKeyStatus.Name)
            : string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyAdded, accessKeyStatus.Name);

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

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        if (requestCode == RequestVpnPermissionId && resultCode == Result.Ok)
            VpnDevice.VpnPermissionGranted();
        else
            VpnDevice.VpnPermissionRejected();
    }

    protected override void OnDestroy()
    {
        VpnDevice.OnRequestVpnPermission -= Device_OnRequestVpnPermission;

        base.OnDestroy();
    }

}