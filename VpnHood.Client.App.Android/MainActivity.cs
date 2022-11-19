#nullable enable
using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VpnHood.Client.App.UI;
using VpnHood.Client.Device.Android;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Android;


[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    Exported = true,
    MainLauncher = true, AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]
[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
public class MainActivity : Activity
{
    private const int RequestVpnPermission = 10;
    private VpnHoodAppUi? _appUi;

    private AndroidDevice Device =>
        (AndroidDevice?)AndroidApp.Current?.Device ?? throw new InvalidOperationException($"{nameof(Device)} is not initialized!");

    public WebView? WebView { get; private set; }
    public Color BackgroundColor
    {
        get
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                return Resources?.GetColor(Resource.Color.colorBackground, Theme) ?? Color.DarkBlue;

#pragma warning disable 618
            return Resources?.GetColor(Resource.Color.colorBackground) ?? Color.DarkBlue;
#pragma warning restore 618
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitSplashScreen();

        // manage VpnPermission
        Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

        // Initialize UI
        var zipStream = Resources?.Assets?.Open("SPA.zip") ?? throw new Exception("Could not load SPA.zip resource!");
        _appUi = VpnHoodAppUi.Init(zipStream);
        InitWebUi();
    }

    private void Device_OnRequestVpnPermission(object sender, EventArgs e)
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
        _appUi?.Dispose();
        _appUi = null;
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    private void InitSplashScreen()
    {
        var imageView = new ImageView(this);
        imageView.SetImageResource(Resource.Mipmap.ic_launcher_round);
        imageView.LayoutParameters = new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        imageView.SetScaleType(ImageView.ScaleType.CenterInside);
        imageView.SetBackgroundColor(BackgroundColor);
        SetContentView(imageView);
    }

    private void InitWebUi()
    {
        WebView = new WebView(this);
        WebView.SetBackgroundColor(BackgroundColor);
        WebView.Settings.JavaScriptEnabled = true;
        WebView.Settings.DomStorageEnabled = true;
        WebView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        WebView.Settings.SetSupportMultipleWindows(true);
        WebView.SetLayerType(LayerType.Hardware, null);

        var webViewClient = new MyWebViewClient();
        webViewClient.PageLoaded += WebViewClient_PageLoaded;
        WebView.SetWebViewClient(webViewClient);
        WebView.SetWebChromeClient(new MyWebChromeClient());

#if DEBUG
        WebView.SetWebContentsDebuggingEnabled(true);
#endif
        if (_appUi == null) throw new Exception($"{_appUi} is not initialized!");
        WebView.LoadUrl($"{_appUi.Url}?nocache={_appUi.SpaHash}");
    }

    private void WebViewClient_PageLoaded(object sender, EventArgs e)
    {
        if (WebView == null) throw new Exception("WebView has not been loaded yet!");
        SetContentView(WebView);
        Window?.SetStatusBarColor(BackgroundColor);
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



