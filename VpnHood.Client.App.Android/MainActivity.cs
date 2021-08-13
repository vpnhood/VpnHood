#nullable enable
using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using Android.Net;
using Android.Content;
using Android.Widget;
using Android.Views;
using Android.Graphics;
using VpnHood.Client.App.UI;
using Android.Webkit;
using VpnHood.Client.Device.Android;
using System;

namespace VpnHood.Client.App.Android
{
    [Activity(Label = "@string/app_name",
        Icon = "@mipmap/ic_launcher",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        MainLauncher = true, AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
        ScreenOrientation = ScreenOrientation.UserPortrait,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]
    public class MainActivity : Activity
    {
        private VpnHoodAppUI? _appUi;
        private const int REQUEST_VpnPermission = 10;
        private AndroidDevice Device => (AndroidDevice?)AndroidApp.Current?.Device ?? throw new InvalidOperationException($"{nameof(Device)} is not initialized!");
        
        public WebView? WebView { get; private set; }
        public Color BackgroudColor => Resources?.GetColor(Resource.Color.colorBackground, null) ?? Color.DarkBlue;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // initialize web view
            InitSplashScreen();

            // manage VpnPermission
            Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

            // Initialize UI
            var zipStream = Resources?.Assets?.Open("SPA.zip") ?? throw new Exception("Could not load SPA.zip resource!");
            _appUi = VpnHoodAppUI.Init(zipStream);
            InitWebUI();
        }

        private void Device_OnRequestVpnPermission(object sender, System.EventArgs e)
        {
            var intent = VpnService.Prepare(this);
            if (intent == null)
            {
                Device.VpnPermissionGranted();
            }
            else
            {
                StartActivityForResult(intent, REQUEST_VpnPermission);
            }
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
        {
            if (requestCode == REQUEST_VpnPermission && resultCode == Result.Ok)
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

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        private void InitSplashScreen()
        {
            var imageView = new ImageView(this);
            imageView.SetImageResource(Resource.Mipmap.ic_launcher_round);
            imageView.LayoutParameters = new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            imageView.SetScaleType(ImageView.ScaleType.CenterInside);
            //imageView.SetBackgroundColor(Color);
            SetContentView(imageView);
        }

        private void InitWebUI()
        {
            WebView = new WebView(this);
            WebView.SetBackgroundColor(BackgroudColor);
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

        private void WebViewClient_PageLoaded(object sender, System.EventArgs e)
        {
            if (WebView == null) throw new Exception("WebView has not been loaded yet!");
            SetContentView(WebView);
            Window?.SetStatusBarColor(BackgroudColor);
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
}