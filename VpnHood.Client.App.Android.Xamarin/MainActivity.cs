// ReSharper disable once RedundantNullableDirective
#nullable enable
using System;
using System.IO;
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
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataScheme = "content", DataMimeTypes = new[] { AccessKeyMime1, AccessKeyMime2, AccessKeyMime3 })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataSchemes = new[] { AccessKeyScheme1, AccessKeyScheme2 })]
public class MainActivity : Activity
{
    private const int RequestVpnPermission = 10;
    public const string AccessKeyScheme1 = "vh";
    public const string AccessKeyScheme2 = "vhkey";
    public const string AccessKeyMime1 = "application/vhkey";
    public const string AccessKeyMime2 = "application/key";
    public const string AccessKeyMime3 = "application/vnd.cinderella";

    private AndroidDevice Device =>
        (AndroidDevice?)App.Current?.AppProvider.Device ?? throw new InvalidOperationException($"{nameof(Device)} is not initialized!");

    public WebView? WebView { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitSplashScreen();

        // manage VpnPermission
        Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit)
        {
            using var memoryStream = new MemoryStream(UiResource.SPA);
            VpnHoodAppWebServer.Init(memoryStream);
        }

        // process intent
        ProcessIntent(Intent);

        InitWebUi();
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
            Device.VpnPermissionGranted();
        else
            StartActivityForResult(intent, RequestVpnPermission);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        if (requestCode == RequestVpnPermission && resultCode == Result.Ok)
            Device.VpnPermissionGranted();
        else
            Device.VpnPermissionRejected();
    }

    protected override void OnDestroy()
    {
        Device.OnRequestVpnPermission -= Device_OnRequestVpnPermission;
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
        if (WebView == null) throw new Exception("WebView has not been loaded yet!");
        SetContentView(WebView);
        Window?.SetStatusBarColor(App.BackgroundColor);
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
