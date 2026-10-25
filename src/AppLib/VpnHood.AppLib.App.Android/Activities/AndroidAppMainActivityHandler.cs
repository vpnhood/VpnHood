using Android.Content;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.AppLib.App.Android.Utils;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using Permission = Android.Content.PM.Permission;

namespace VpnHood.AppLib.App.Android.Activities;

public class AndroidAppMainActivityHandler
{
    private readonly string[] _accessKeySchemes;
    private readonly string[] _accessKeyMimes;
    private readonly Task<VhColor?> _chromeColor;
    protected IActivityEvent ActivityEvent { get; }

    // chromeColor: the UI's colour for the system bars and the ground behind them, from the UI that
    // hosts this activity, as it becomes known; null, and they keep the system's.
    public AndroidAppMainActivityHandler(IActivityEvent activityEvent, AndroidMainActivityOptions options,
        Task<VhColor?> chromeColor)
    {
        ActivityEvent = activityEvent;
        _accessKeySchemes = options.AccessKeySchemes;
        _accessKeyMimes = options.AccessKeyMimes;
        _chromeColor = chromeColor;

        activityEvent.CreateEvent += (_, args) => OnCreate(args.SavedInstanceState);
        activityEvent.NewIntentEvent += (_, args) => OnNewIntent(args.Intent);
        activityEvent.RequestPermissionsResultEvent += (_, args) =>
            OnRequestPermissionsResult(args.RequestCode, args.Permissions, args.GrantResults);
        activityEvent.ActivityResultEvent +=
            (_, args) => OnActivityResult(args.RequestCode, args.ResultCode, args.Data);
        activityEvent.KeyDownEvent += (_, args) => args.IsHandled = OnKeyDown(args.KeyCode, args.KeyEvent);
        activityEvent.PauseEvent += (_, _) => OnPause();
        activityEvent.ResumeEvent += (_, _) => OnResume();
        activityEvent.DestroyEvent += (_, _) => OnDestroy();
        activityEvent.ConfigurationChangedEvent += (_, args) => OnConfigurationChanged(args);
    }

    protected virtual void OnCreate(Bundle? savedInstanceState)
    {
        _ = savedInstanceState;

        AppUiContext.Context = new AndroidUiContext(ActivityEvent);

        // initialize the window
        VhInvoker.TryInvoke("configure the window insets", LogLevel.Warning, () =>
            WindowInsetUtils.Configure(ActivityEvent.Activity.Window, false, false, false, _chromeColor));

        // process intent
        ProcessIntent(ActivityEvent.Activity.Intent);
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
        try {
            var uri = intent.Data;
            if (_accessKeySchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) {
                ImportAccessKey(uri.ToString()!);
                return true;
            }

            // check mime
            var mimeType = ActivityEvent.Activity.ContentResolver.GetType(uri);
            if (!_accessKeyMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase)) {
                Toast.MakeText(ActivityEvent.Activity, VpnHoodApp.Instance.Resources.Strings.MsgUnsupportedContent,
                    ToastLength.Long)?.Show();
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
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not import the access key of a link or a file.");
            Toast.MakeText(ActivityEvent.Activity, VpnHoodApp.Instance.Resources.Strings.MsgCantReadAccessKey,
                ToastLength.Long)?.Show();
        }

        return true;
    }

    protected void ImportAccessKey(string accessKey)
    {
        var profiles = VpnHoodApp.Instance.VpnProfileService.List();
        var profileInfo = VpnHoodApp.Instance.VpnProfileService.ImportAccessKey(accessKey).ToInfo(VpnHoodApp.Instance.Features);

        // the key's server becomes the chosen one; the save keeps it, and moves the configuration time,
        // on which a UI reads the servers and the settings again
        VpnHoodApp.Instance.UserSettings.VpnProfileId = profileInfo.VpnProfileId;
        VpnHoodApp.Instance.SettingsService.Save();

        var isNew = profiles.All(x => x.VpnProfileId != profileInfo.VpnProfileId);
        var message = isNew
            ? string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyAdded, profileInfo.VpnProfileName)
            : string.Format(VpnHoodApp.Instance.Resources.Strings.MsgAccessKeyUpdated, profileInfo.VpnProfileName);

        Toast.MakeText(ActivityEvent.Activity, message, ToastLength.Long)?.Show();
    }

    protected virtual void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        _ = requestCode;
        _ = permissions;
        _ = grantResults;
    }

    protected virtual bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? keyEvent)
    {
        _ = keyEvent;
        return false;
    }

    protected virtual void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        _ = requestCode;
        _ = resultCode;
        _ = data;
    }

    protected virtual void OnConfigurationChanged(Configuration args)
    {
        _ = args;
        VpnHoodApp.Instance.UpdateUi();
    }

    // The activity in front is the app's UI context. An intent the task did not start with stacks a
    // second activity, which takes the context in its OnCreate; Back resumes the one beneath before
    // the second's OnDestroy, so the context comes back here and that OnDestroy leaves it.
    protected virtual void OnResume()
    {
        var appUiContext = (AndroidUiContext?)AppUiContext.Context;
        if (appUiContext?.ActivityEvent != ActivityEvent)
            AppUiContext.Context = new AndroidUiContext(ActivityEvent);
    }

    protected virtual void OnPause()
    {
    }

    protected virtual void OnDestroy()
    {
        // prevent multiple launch clear the activity context
        var appUiContext = (AndroidUiContext?)AppUiContext.Context;
        if (appUiContext?.ActivityEvent == ActivityEvent)
            AppUiContext.Context = null;
    }
}