using Android.Content;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.AppLib.App.Android.Utils;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Abstractions.UiContexts;
using Permission = Android.Content.PM.Permission;

namespace VpnHood.AppLib.App.Android.Activities;

/// <summary>
/// Provides the app's work for a main activity: its UI context, the window's insets, the access keys of links and files,
/// and the activity's events.
/// </summary>
/// <remarks>It follows the activity's events from its construction; a UI's handler derives from it.</remarks>
public class AndroidAppMainActivityHandler
{
    private readonly string[] _accessKeySchemes;
    private readonly string[] _accessKeyMimes;
    private readonly Task<VhColor?> _chromeColor;

    /// <summary>Gets the activity, as the source of its events.</summary>
    protected IActivityEvent ActivityEvent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AndroidAppMainActivityHandler"/> class, which follows the activity's
    /// events.
    /// </summary>
    /// <param name="activityEvent">The activity.</param>
    /// <param name="options">The access keys the activity takes.</param>
    /// <param name="chromeColor">
    /// A task whose result is the UI's colour for the system bars and the ground behind them, as it becomes known; with a
    /// <see langword="null"/> result, no background of the UI's is painted.
    /// </param>
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

    /// <summary>
    /// Called when the activity is created: it takes the app's UI context, configures the window's insets, and imports an
    /// access key the intent carries.
    /// </summary>
    /// <param name="savedInstanceState">The state Android saved, or <see langword="null"/>.</param>
    protected virtual void OnCreate(Bundle? savedInstanceState)
    {
        _ = savedInstanceState;

        TakeUiContext();

        // initialize the window
        VhInvoker.TryInvoke("configure the window insets", LogLevel.Warning, () =>
            WindowInsetUtils.Configure(ActivityEvent.Activity.Window, false, false, false, _chromeColor));

        // process intent
        ProcessIntent(ActivityEvent.Activity.Intent);
    }

    /// <summary>Called when the activity gets a new intent: it imports an access key the intent carries.</summary>
    /// <param name="intent">The intent, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> where it tried the intent's link or file, whether or not the import worked;
    /// <see langword="false"/> where the intent carries neither, the activity has no content resolver, or a file is of a type
    /// the activity does not take.
    /// </returns>
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

    /// <summary>Imports an access key, makes its server the chosen one, and says so in a toast.</summary>
    /// <param name="accessKey">The access key.</param>
    /// <remarks>A UI that imports keys itself, and says so in its own way, overrides it.</remarks>
    /// <exception cref="FormatException">
    /// The access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="NotSupportedException">The access key's version is not one the app reads.</exception>
    /// <exception cref="System.Text.Json.JsonException">The text holds no valid access key.</exception>
    /// <exception cref="InvalidDataException">The access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="OverflowException">A duration in the access key is out of range.</exception>
    /// <exception cref="UnauthorizedAccessException">The access key is a built-in VPN profile's.</exception>
    protected virtual void ImportAccessKey(string accessKey)
    {
        var profiles = VpnHoodApp.Instance.VpnProfileService.List();
        var profileInfo = VpnHoodApp.Instance.VpnProfileService.ImportAccessKey(accessKey).ToInfo(VpnHoodApp.Instance.Descriptor);

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

    /// <summary>Called with a permission request's result; it does nothing by default.</summary>
    /// <param name="requestCode">The request's code.</param>
    /// <param name="permissions">The permissions asked for.</param>
    /// <param name="grantResults">Whether each was granted.</param>
    protected virtual void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        _ = requestCode;
        _ = permissions;
        _ = grantResults;
    }

    /// <summary>Called when a key is pressed; it handles none by default.</summary>
    /// <param name="keyCode">The key.</param>
    /// <param name="keyEvent">The key's event, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> where the handler handled the key.</returns>
    protected virtual bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? keyEvent)
    {
        _ = keyEvent;
        return false;
    }

    /// <summary>Called with the result of an activity this one started; it does nothing by default.</summary>
    /// <param name="requestCode">The request's code.</param>
    /// <param name="resultCode">The result.</param>
    /// <param name="data">The result's data, or <see langword="null"/>.</param>
    protected virtual void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        _ = requestCode;
        _ = resultCode;
        _ = data;
    }

    /// <summary>Called when the activity's configuration changed: it has the app update its UI.</summary>
    /// <param name="args">The new configuration.</param>
    protected virtual void OnConfigurationChanged(Configuration args)
    {
        _ = args;
        VpnHoodApp.Instance.UpdateUi();
    }

    /// <summary>Called when the activity comes to the front: it takes the app's UI context back.</summary>
    protected virtual void OnResume()
    {
        TakeUiContext();
    }

    /// <summary>
    /// Makes this activity the app's UI context, unless it is already, as a context made again would tell the app of a new
    /// window, which may start an ad's preload and an update check once more.
    /// </summary>
    /// <remarks>An activity whose view asks for the context as it is built takes it before its own create builds that view.</remarks>
    public void TakeUiContext()
    {
        var appUiContext = (AndroidUiContext?)AppUiContext.Context;
        if (appUiContext?.ActivityEvent != ActivityEvent)
            AppUiContext.Context = new AndroidUiContext(ActivityEvent);
    }

    /// <summary>Called when the activity leaves the front; it does nothing by default.</summary>
    protected virtual void OnPause()
    {
    }

    /// <summary>Called when the activity is destroyed: it lets the app's UI context go where it is this activity's.</summary>
    protected virtual void OnDestroy()
    {
        // prevent multiple launch clear the activity context
        var appUiContext = (AndroidUiContext?)AppUiContext.Context;
        if (appUiContext?.ActivityEvent == ActivityEvent)
            AppUiContext.Context = null;
    }
}