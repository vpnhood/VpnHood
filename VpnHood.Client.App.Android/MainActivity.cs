using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using Android.Net;
using Android.Content;
using Android.Widget;
using Android.Views;
using VpnHood.Client.App.UI;
using Android.Webkit;

namespace VpnHood.Client.Droid
{
    [Activity(Label = "VpnHoodApp",
        Icon = "@mipmap/ic_launcher",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        MainLauncher = true, AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]
    public class MainActivity : Activity
    {
        class MyWebViewClient : WebViewClient
        {
            private readonly MainActivity _mainActivity;
            public MyWebViewClient(MainActivity mainActivity) { _mainActivity = mainActivity;}
            public override bool ShouldOverrideUrlLoading(WebView webView, IWebResourceRequest request)
            {
                return ShouldOverrideUrlLoading(webView, request);
            }

            public override void OnPageFinished(WebView view, string url)
            {
                base.OnPageFinished(view, url);
                _mainActivity.SetContentView(_mainActivity._webView);
            }

            public override void OnPageCommitVisible(WebView view, string url) => base.OnPageCommitVisible(view, url);
        }

        private WebView _webView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            Window.RequestFeature(WindowFeatures.NoTitle);
            base.OnCreate(savedInstanceState);

            AndroidApp.Current.MainActivity = this;

            //initialize web view
            InitSplashScreen();
            InitWebUI();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            if (resultCode == Result.Ok)
            {
                var serviceIntent = new Intent(this, typeof(AppVpnService));
                StartService(serviceIntent.SetAction("connect"));
            }
        }

        public void StartVpn()
        {
            var intent = VpnService.Prepare(this);
            if (intent != null)
            {
                StartActivityForResult(intent, 0);
            }
            else
            {
                OnActivityResult(0, Result.Ok, null);
            }
        }

        private void InitSplashScreen()
        {
            var imageView = new ImageView(this);
            imageView.SetImageResource(Resource.Mipmap.ic_launcher);
            imageView.LayoutParameters = new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            imageView.SetScaleType(ImageView.ScaleType.CenterInside);
            //imageView.SetBackgroundColor(Android.Graphics.Color.Blue);
            SetContentView(imageView);
        }

        private void InitWebUI()
        {
            if (!VpnHoodAppUI.Current.Started)
                VpnHoodAppUI.Current.Start().GetAwaiter();

            _webView = new WebView(this);
            _webView.SetWebViewClient(new MyWebViewClient(this));
            _webView.Settings.JavaScriptEnabled = true;
            _webView.Settings.DomStorageEnabled = true;
            _webView.Settings.SetSupportMultipleWindows(true);
            _webView.SetLayerType(LayerType.Hardware, null);
#if DEBUG
            WebView.SetWebContentsDebuggingEnabled(true);
#endif
            _webView.LoadUrl($"{VpnHoodAppUI.Current.Url}?nocache={VpnHoodAppUI.Current.SpaHash}");
        }

        public override void OnBackPressed()
        {
            if (_webView.CanGoBack())
                _webView.GoBack();
            else
                base.OnBackPressed();
        }


    }
}