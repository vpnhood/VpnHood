using System;
using System.IO;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid;

[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "content", DataMimeTypes = new[] { AccessKeyMime1, AccessKeyMime2, AccessKeyMime3 })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataSchemes = new[] { AccessKeyScheme1, AccessKeyScheme2 })]
[IntentFilter(new[] { Android.Service.QuickSettings.TileService.ActionQsTilePreferences })]
public class MainActivity : Activity
{
    private bool _isWeViewVisible;
    private const int RequestPushNotificationId = 11;
    private const int RequestVpnPermissionId = 10;
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey";
    public const string AccessKeyMime2 = "application/key";
    public const string AccessKeyMime3 = "application/vnd.cinderella";

    private static AndroidDevice VpnDevice => App.Current?.VpnDevice ?? throw new InvalidOperationException($"{nameof(App)} has not been initialized.");

    public WebView? WebView { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitSplashScreen();
        Window?.SetStatusBarColor(App.BackgroundColor);
        Window?.SetNavigationBarColor(App.BackgroundColor);

        // manage VpnPermission
        VpnDevice.OnRequestVpnPermission += Device_OnRequestVpnPermission;

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit)
        {
            using var memoryStream = new MemoryStream(UiResource.SPA);
            VpnHoodAppWebServer.Init(memoryStream);
        }

        // process intent
        ProcessIntent(Intent);

        InitWebUi();

        // request features
        _ = RequestFeatures();
    }

    private async Task RequestFeatures()
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
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, RequestPushNotificationId);
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
            if (uri.Scheme is AccessKeyScheme1 or AccessKeyScheme2)
            {
                ImportAccessKey(uri.ToString()!);
                return true;
            }

            // check mime
            var mimeType = ContentResolver.GetType(uri);
            if (mimeType != AccessKeyMime1 && mimeType != AccessKeyMime2 && mimeType != AccessKeyMime1 && mimeType != AccessKeyMime3)
            {
                Toast.MakeText(this, UiResource.MsgUnsupportedContent, ToastLength.Long)?.Show();
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
            Toast.MakeText(this, UiResource.MsgCantReadAccessKey, ToastLength.Long)?.Show();
        }

        return true;
    }

    private void ImportAccessKey(string accessKey)
    {
        var accessKeyStatus = VpnHoodApp.Instance.ClientProfileStore.GetAccessKeyStatus(accessKey);
        var profile = VpnHoodApp.Instance.ClientProfileStore.AddAccessKey(accessKey);
        _ = VpnHoodApp.Instance.Disconnect(true);
        VpnHoodApp.Instance.UserSettings.DefaultClientProfileId = profile.ClientProfileId;

        var message = accessKeyStatus.ClientProfile != null
            ? string.Format(UiResource.MsgAccessKeyUpdated, accessKeyStatus.Name)
            : string.Format(UiResource.MsgAccessKeyAdded, accessKeyStatus.Name);

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
        if (VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Instance.Dispose();

        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {

        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    private void InitSplashScreen()
    {
        var imageView = new ImageView(this);
        // ReSharper disable once AccessToStaticMemberViaDerivedType
        imageView.SetImageResource(Resource.Mipmap.appicon);
        imageView.LayoutParameters = new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        imageView.SetScaleType(ImageView.ScaleType.CenterInside);
        imageView.SetBackgroundColor(App.BackgroundColor);
        SetContentView(imageView);
    }

    private void InitWebUi()
    {
        WebView = new WebView(this);
        WebView.SetBackgroundColor(App.BackgroundColor);
        WebView.Settings.JavaScriptEnabled = true;
        WebView.Settings.DomStorageEnabled = true;
        WebView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        WebView.Settings.SetSupportMultipleWindows(true);
        WebView.SetLayerType(LayerType.Hardware, null);
        
        var webViewClient = new AppWebViewClient();
        webViewClient.PageLoaded += WebViewClient_PageLoaded;
        WebView.SetWebViewClient(webViewClient);
        WebView.SetWebChromeClient(new AppWebChromeClient());

#if DEBUG
        WebView.SetWebContentsDebuggingEnabled(true);
#endif
        WebView.LoadUrl($"{VpnHoodAppWebServer.Instance.Url}?nocache={VpnHoodAppWebServer.Instance.SpaHash}");
    }

    private void WebViewClient_PageLoaded(object? sender, EventArgs e)
    {
        if (_isWeViewVisible) return; // prevent double set SetContentView
        if (WebView == null) throw new Exception("WebView has not been loaded yet!");
        SetContentView(WebView);
        _isWeViewVisible = true;

        Window?.SetNavigationBarColor(App.BackgroundBottomColor);
    }

    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && WebView?.CanGoBack() == true)
        {
            WebView.GoBack();
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }
}
