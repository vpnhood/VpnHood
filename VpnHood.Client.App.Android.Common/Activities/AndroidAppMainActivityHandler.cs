using Android;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidAppMainActivityHandler(
    Activity activity, 
    AndroidMainActivityOptions options) 
    : IActivityEvent
{
    private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;
    private readonly string[] _accessKeySchemes = options.AccessKeySchemes;
    private readonly string[] _accessKeyMimes = options.AccessKeySchemes;
    private readonly IAppUpdaterService? _appUpdaterService = options.AppUpdaterService;
    private const int RequestPostNotificationId = 11;
    protected AndroidDevice VpnDevice => AndroidDevice.Current ?? throw new InvalidOperationException($"{nameof(AndroidDevice)} has not been initialized.");
    protected Activity Activity { get; } = activity;
    public event EventHandler<ActivityResultEventArgs>? OnActivityResultEvent;
    public event EventHandler? OnDestroyEvent;

    public virtual void OnCreate(Bundle? savedInstanceState)
    {
        VpnDevice.Prepare(Activity, this);

        // process intent
        ProcessIntent(Activity.Intent);
    }

    protected async Task RequestFeatures()
    {
        // request for adding tile
        if (!VpnHoodApp.Instance.Settings.IsQuickLaunchRequested &&
            OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            VpnHoodApp.Instance.Settings.IsQuickLaunchRequested = true;
            VpnHoodApp.Instance.Settings.Save();
            await QuickLaunchTileService.RequestAddTile(Activity);
        }

        // request for notification
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && Activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
            Activity.RequestPermissions([Manifest.Permission.PostNotifications], RequestPostNotificationId);
            await _requestPostNotificationsCompletionTask.Task;
        }

        // Check for update
        VpnHoodApp.Instance.AppUpdaterService = _appUpdaterService;
        if (VpnHoodApp.Instance.VersionCheckRequired && _appUpdaterService != null && await _appUpdaterService.Update())
            VpnHoodApp.Instance.VersionCheckPostpone(); // postpone check if check succeeded
    }

    public bool OnNewIntent(Intent? intent)
    {
        return ProcessIntent(intent);
    }

    private bool ProcessIntent(Intent? intent)
    {
        if (intent?.Data == null || Activity.ContentResolver == null)
            return false;

        // try to add the access key
        try
        {
            var uri = intent.Data;
            if (_accessKeySchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                ImportAccessKey(uri.ToString()!);
                return true;
            }

            // check mime
            var mimeType = Activity.ContentResolver.GetType(uri);
            if (!_accessKeyMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            {
                Toast.MakeText(Activity, VpnHoodApp.Instance.Resources.Strings.MsgUnsupportedContent, ToastLength.Long)?.Show();
                return false;
            }

            // open stream
            using var inputStream = Activity.ContentResolver.OpenInputStream(uri);
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
            Toast.MakeText(Activity, VpnHoodApp.Instance.Resources.Strings.MsgCantReadAccessKey, ToastLength.Long)?.Show();
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

        Toast.MakeText(Activity, message, ToastLength.Long)?.Show();
    }

    public void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        var postNotificationsIndex = Array.IndexOf(permissions, Manifest.Permission.PostNotifications);
        if (postNotificationsIndex != -1 && _requestPostNotificationsCompletionTask != null)
            _requestPostNotificationsCompletionTask.TrySetResult(grantResults[postNotificationsIndex]);
    }

    public virtual bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        return false;
    }

    public virtual void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        OnActivityResultEvent?.Invoke(this, new ActivityResultEventArgs
        {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });
    }

    public virtual void OnDestroy()
    {
        OnDestroyEvent?.Invoke(this, EventArgs.Empty);
        VpnHoodApp.Instance.AppUpdaterService = null;
    }
}