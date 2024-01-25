using Android.Runtime;
using Android.Views;
using Android.Webkit;
using VpnHood.Client.App.WebServer;

namespace VpnHood.Client.App.Droid.Common.Activities;

public abstract class AndroidAppWebViewMainActivity : AndroidAppMainActivity
{
    private bool _isWeViewVisible;
    public WebView? WebView { get; private set; }
    protected virtual bool ListenToAllIps => false;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitSplashScreen();

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit)
        {
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resources.SpaZipData);
            using var memoryStream = new MemoryStream(VpnHoodApp.Instance.Resources.SpaZipData);
            VpnHoodAppWebServer.Init(memoryStream, listenToAllIps: ListenToAllIps);
        }

        InitWebUi();
    }


    protected override void OnDestroy()
    {
        if (VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Instance.Dispose();

        base.OnDestroy();
    }

    private void InitSplashScreen()
    {
        var imageView = new ImageView(this);
        var appInfo = Application.Context.ApplicationInfo ?? throw new Exception("Could not retrieve app info");
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor?.ToAndroidColor();

        // set splash screen background color
        var icon = appInfo.LoadIcon(Application.Context.PackageManager);
        imageView.SetImageDrawable(icon);
        imageView.LayoutParameters = new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        imageView.SetScaleType(ImageView.ScaleType.CenterInside);
        if (backgroundColor != null) imageView.SetBackgroundColor(backgroundColor.Value);
        SetContentView(imageView);

        // set window background color
        if (backgroundColor != null)
        {
            Window?.SetStatusBarColor(backgroundColor.Value);
            Window?.SetNavigationBarColor(backgroundColor.Value);
        }
    }

    private void InitWebUi()
    {
        WebView = new WebView(this);
        WebView.Settings.JavaScriptEnabled = true;
        WebView.Settings.DomStorageEnabled = true;
        WebView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        WebView.Settings.SetSupportMultipleWindows(true);
        WebView.SetLayerType(LayerType.Hardware, null);
        if (VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor != null)
            WebView.SetBackgroundColor(VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor.Value.ToAndroidColor());

        var webViewClient = new AndroidAppWebViewClient();
        webViewClient.PageLoaded += WebViewClient_PageLoaded;
        WebView.SetWebViewClient(webViewClient);
        WebView.SetWebChromeClient(new AndroidAppWebChromeClient());

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

        if (VpnHoodApp.Instance.Resources.Colors.NavigationBarColor != null)
            Window?.SetNavigationBarColor(VpnHoodApp.Instance.Resources.Colors.NavigationBarColor.Value.ToAndroidColor());

        // request features after loading the webview, so SPA can update the localize the resources
        _ = RequestFeatures();
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
