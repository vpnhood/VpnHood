using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.Device.Droid.ActivityEvents;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidAppWebViewMainActivityHandler(
    IActivityEvent activityEvent,
    AndroidMainActivityWebViewOptions options)
    : AndroidAppMainActivityHandler(activityEvent, options)
{
    private bool _isWeViewVisible;
    private WebView? WebView { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitLoadingPage();

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit)
        {
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resource.SpaZipData);
            using var memoryStream = new MemoryStream(VpnHoodApp.Instance.Resource.SpaZipData);
            VpnHoodAppWebServer.Init(memoryStream, options.DefaultSpaPort, listenToAllIps: options.ListenToAllIps);
        }

        InitWebUi();
    }

    private void InitLoadingPage()
    {
        ActivityEvent.Activity.SetContentView(_Microsoft.Android.Resource.Designer.Resource.Layout.progressbar);
            
        // set window background color
        var linearLayout = ActivityEvent.Activity.FindViewById<LinearLayout>(_Microsoft.Android.Resource.Designer.Resource.Id.myLayout);
        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (linearLayout != null && backgroundColor != null)
        {
            try { linearLayout.SetBackgroundColor(backgroundColor.Value); }
            catch { /* ignore */ }

            try { ActivityEvent.Activity.Window?.SetStatusBarColor(backgroundColor.Value); }
            catch { /* ignore */ }
            
            try { ActivityEvent.Activity.Window?.SetNavigationBarColor(backgroundColor.Value); }
            catch { /* ignore */ }
        }
        
        // set progressbar color
        var progressBarColor = VpnHoodApp.Instance.Resource.Colors.ProgressBarColor?.ToAndroidColor();
        var progressBar = ActivityEvent.Activity.FindViewById<ProgressBar>(_Microsoft.Android.Resource.Designer.Resource.Id.progressBar);
        if (progressBar != null && progressBarColor != null)
        {
            try 
            { progressBar.IndeterminateTintList = ColorStateList.ValueOf(progressBarColor.Value); }
            catch { /* ignore */ }   
        }
    }

    private void InitWebUi()
    {
        WebView = new WebView(ActivityEvent.Activity);
        WebView.Settings.JavaScriptEnabled = true;
        WebView.Settings.DomStorageEnabled = true;
        WebView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        WebView.Settings.SetSupportMultipleWindows(true);
        WebView.SetLayerType(LayerType.Hardware, null);
        if (VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor != null)
            WebView.SetBackgroundColor(VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor.Value.ToAndroidColor());

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
        ActivityEvent.Activity.SetContentView(WebView);
        _isWeViewVisible = true;

        if (VpnHoodApp.Instance.Resource.Colors.NavigationBarColor != null)
            ActivityEvent.Activity.Window?.SetNavigationBarColor(VpnHoodApp.Instance.Resource.Colors.NavigationBarColor.Value.ToAndroidColor());
    }

    protected override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && WebView?.CanGoBack() == true)
        {
            WebView.GoBack();
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }
}
