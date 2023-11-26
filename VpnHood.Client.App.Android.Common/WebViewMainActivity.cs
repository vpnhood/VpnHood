using System;
using System.IO;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using VpnHood.Client.App.WebServer;

namespace VpnHood.Client.App.Droid.Common;

public abstract class WebViewMainActivity : VpnHoodMainActivity
{
    private readonly byte[] _spaZipBuffer;
    private bool _isWeViewVisible;
    
    protected WebViewMainActivity(byte[] spaZipBuffer)
    {
        _spaZipBuffer = spaZipBuffer;
    }

    public WebView? WebView { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitSplashScreen();

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit)
        {
            using var memoryStream = new MemoryStream(_spaZipBuffer);
            VpnHoodAppWebServer.Init(memoryStream);
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
        var icon = appInfo.LoadIcon(Application.Context.PackageManager);
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor?.ToAndroidColor();

        // set splash screen background color
        imageView.SetImageDrawable(icon); //todo test
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

        if (VpnHoodApp.Instance.Resources.Colors.WindowBackgroundBottomColor != null)
            Window?.SetNavigationBarColor(VpnHoodApp.Instance.Resources.Colors.WindowBackgroundBottomColor.Value.ToAndroidColor());

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
