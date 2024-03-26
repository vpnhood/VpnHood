using Android;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidAppMainActivityHandler 
{
    private TaskCompletionSource<Permission>? _requestPostNotificationsCompletionTask;
    private readonly string[] _accessKeySchemes;
    private readonly string[] _accessKeyMimes;
    private readonly IAppUpdaterService? _appUpdaterService;
    private const int RequestPostNotificationId = 11;
    protected IActivityEvent ActivityEvent { get; }
    protected virtual bool RequestFeaturesOnCreate { get; }

    public AndroidAppMainActivityHandler(IActivityEvent activityEvent, AndroidMainActivityOptions options)
    {
        ActivityEvent = activityEvent;
        _appUpdaterService = options.AppUpdaterService;
        _accessKeySchemes = options.AccessKeySchemes;
        _accessKeyMimes = options.AccessKeySchemes;
        RequestFeaturesOnCreate = options.RequestFeaturesOnCreate;


        activityEvent.CreateEvent += (_, args) => OnCreate(args.SavedInstanceState);
        activityEvent.NewIntentEvent += (_, args) => OnNewIntent(args.Intent);
        activityEvent.RequestPermissionsResultEvent += (_, args) => OnRequestPermissionsResult(args.RequestCode, args.Permissions, args.GrantResults);
        activityEvent.ActivityResultEvent += (_, args) => OnActivityResult(args.RequestCode, args.ResultCode, args.Data);
        activityEvent.KeyDownEvent += (_, args) => args.IsHandled = OnKeyDown(args.KeyCode, args.KeyEvent);
        activityEvent.DestroyEvent += (_, _) => OnDestroy();
        activityEvent.ConfigurationChangedEvent += (_, args) => OnConfigurationChanged(args);
    }

    protected virtual void OnCreate(Bundle? savedInstanceState)
    {
        AndroidDevice.Instance.Prepare(ActivityEvent);

        // process intent
        ProcessIntent(ActivityEvent.Activity.Intent);

        if (RequestFeaturesOnCreate)
            _ = RequestFeatures();
    }

    public async Task RequestFeatures()
    {
        // request for adding tile
        if (!VpnHoodApp.Instance.Settings.IsQuickLaunchRequested &&
            OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            VpnHoodApp.Instance.Settings.IsQuickLaunchRequested = true;
            VpnHoodApp.Instance.Settings.Save();
            await QuickLaunchTileService.RequestAddTile(ActivityEvent.Activity);
        }

        // request for notification
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && ActivityEvent.Activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            _requestPostNotificationsCompletionTask = new TaskCompletionSource<Permission>();
            ActivityEvent.Activity.RequestPermissions([Manifest.Permission.PostNotifications], RequestPostNotificationId);
            await _requestPostNotificationsCompletionTask.Task;
        }

        // Check for update
        VpnHoodApp.Instance.AppUpdaterService = _appUpdaterService;
        if (VpnHoodApp.Instance.VersionCheckRequired && _appUpdaterService != null && await _appUpdaterService.Update())
            VpnHoodApp.Instance.VersionCheckPostpone(); // postpone check if check succeeded
    }

    protected virtual bool OnNewIntent(Intent? intent)
    {
        return ProcessIntent(intent);
    }

    private bool ProcessIntent(Intent? intent)
    {
        if (intent?.Data == null || ActivityEvent.Activity.ContentResolver == null)
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
            var mimeType = ActivityEvent.Activity.ContentResolver.GetType(uri);
            if (!_accessKeyMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            {
                Toast.MakeText(ActivityEvent.Activity, VpnHoodApp.Instance.Resource.Strings.MsgUnsupportedContent, ToastLength.Long)?.Show();
                return false;
            }

            // open stream
            using var inputStream = ActivityEvent.Activity.ContentResolver.OpenInputStream(uri);
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
            Toast.MakeText(ActivityEvent.Activity, VpnHoodApp.Instance.Resource.Strings.MsgCantReadAccessKey, ToastLength.Long)?.Show();
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
            ? string.Format(VpnHoodApp.Instance.Resource.Strings.MsgAccessKeyAdded, profile.ClientProfileName)
            : string.Format(VpnHoodApp.Instance.Resource.Strings.MsgAccessKeyUpdated, profile.ClientProfileName);

        Toast.MakeText(ActivityEvent.Activity, message, ToastLength.Long)?.Show();
    }

    protected virtual void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        var postNotificationsIndex = Array.IndexOf(permissions, Manifest.Permission.PostNotifications);
        if (postNotificationsIndex != -1 && _requestPostNotificationsCompletionTask != null)
            _requestPostNotificationsCompletionTask.TrySetResult(grantResults[postNotificationsIndex]);
    }

    protected virtual bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        return false;
    }

    protected virtual void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
    }

    protected virtual void OnConfigurationChanged(Configuration args)
    {
        VpnHoodApp.Instance.InitUi();
    }

    protected virtual void OnDestroy()
    {
        VpnHoodApp.Instance.AppUpdaterService = null;
    }
}