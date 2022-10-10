#nullable enable
using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VpnHood.Client.App.Android.Ads;
using VpnHood.Client.App.UI;
using VpnHood.Client.Device.Android;
using Xamarin.Essentials;

namespace VpnHood.Client.App.Android;


[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true, AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.UserPortrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]
[IntentFilter(new[] { "android.intent.action.MAIN" }, Categories = new[] { "android.intent.category.LEANBACK_LAUNCHER" })]
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

    private static bool _isInitializedCalled;
    private void InitAds()
    {
        //var intent = new Intent(this, typeof(VpnHoodAdActivity));
        //intent.SetAction(Intent.ActionMain);
        //intent.SetFlags(ActivityFlags.BroughtToFront | ActivityFlags.NewTask | ActivityFlags.SingleTop);
        //intent.AddCategory(Intent.CategoryLauncher);
        //StartActivity(intent);

        try
        {
            if (_isInitializedCalled)
            {
                MobileAds.Initialize(this);
                _isInitializedCalled = true;
            }

            if (!VpnHoodApp.Instance.IsWaitingForAd)
            {
                VpnHoodApp.Instance.IsWaitingForAd = true;
                var adRequest = new AdRequest.Builder().Build();
                InterstitialAd.Load(this, "ca-app-pub-9339227682123409/2322872125", adRequest,
                    new VpnHoodInterstitialAdLoadCallback(this));
            }
        }
        catch
        {
            // ignored
            // Lucky at the moment
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;

        // initialize web view
        InitSplashScreen();

        // manage VpnPermission
        Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

        // Initialize UI
        var zipStream = Resources?.Assets?.Open("SPA.zip") ?? throw new Exception("Could not load SPA.zip resource!");
        _appUi = VpnHoodAppUi.Init(zipStream);
        InitWebUi();
    }

    private void ConnectionStateChanged(object sender, EventArgs e)
    {
        // show ads
        var connectionState = VpnHoodApp.Instance.ConnectionState;
        if (connectionState == AppConnectionState.Connected && 
            VpnHoodApp.Instance.ActiveClientProfile?.TokenId == Guid.Parse("{5aacec55-5cac-457a-acad-3976969236f8}")) //todo: temporary public token
        {
            Handler mainHandler = new Handler(MainLooper!);
            mainHandler.Post(InitAds);
        }
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

    public override void OnBackPressed()
    {
        if (WebView == null) throw new Exception("WebView has not been loaded yet!");

        if (WebView.CanGoBack())
            WebView.GoBack();
        else
            base.OnBackPressed();
    }
}



