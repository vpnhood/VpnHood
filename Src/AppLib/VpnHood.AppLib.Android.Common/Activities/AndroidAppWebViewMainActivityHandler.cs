using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Droid.ActivityEvents;

namespace VpnHood.AppLib.Droid.Common.Activities;

public class AndroidAppWebViewMainActivityHandler(
    IActivityEvent activityEvent,
    AndroidMainActivityWebViewOptions options)
    : AndroidAppMainActivityHandler(activityEvent, options)
{
    private bool _isWeViewVisible;
    private WebView? WebView { get; set; }
    public Exception? WebViewCreateException { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // initialize web view
        InitLoadingPage();

        // Initialize UI
        if (!VpnHoodAppWebServer.IsInit) {
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resource.SpaZipData);
            using var spaZipStream = new MemoryStream(VpnHoodApp.Instance.Resource.SpaZipData);
            VpnHoodAppWebServer.Init(new WebServerOptions {
                SpaZipStream = spaZipStream,
                DefaultPort = options.SpaDefaultPort,
                ListenOnAllIps = options.SpaListenToAllIps
            });
        }

        InitWebUi();
    }

    protected override void OnPause()
    {
        base.OnPause();

        if (!ActiveUiContext.IsPartialIntentRunning)
            WebView?.OnPause();

        // temporarily stop the server to find is the crash belong to embed-io
        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.KillSpaServer) && VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Instance.Stop();
    }

    protected override void OnResume()
    {
        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.KillSpaServer) && VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Instance.Start();

        WebView?.OnResume();
        base.OnResume();
    }

    private void InitLoadingPage()
    {
        ActivityEvent.Activity.SetContentView(_Microsoft.Android.Resource.Designer.Resource.Layout.progressbar);

        // set window background color
        var linearLayout =
            ActivityEvent.Activity.FindViewById<LinearLayout>(_Microsoft.Android.Resource.Designer.Resource.Id
                .myLayout);

        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor?.ToAndroidColor();
        if (linearLayout != null && backgroundColor != null) {
            try {
                linearLayout.SetBackgroundColor(backgroundColor.Value);
            }
            catch {
                /* ignore */
            }

            try {
                ActivityEvent.Activity.Window?.SetStatusBarColor(backgroundColor.Value);
            }
            catch {
                /* ignore */
            }

            try {
                ActivityEvent.Activity.Window?.SetNavigationBarColor(backgroundColor.Value);
            }
            catch {
                /* ignore */
            }
        }

        // set progressbar color
        var progressBarColor = VpnHoodApp.Instance.Resource.Colors.ProgressBarColor?.ToAndroidColor();
        var progressBar =
            ActivityEvent.Activity.FindViewById<ProgressBar>(_Microsoft.Android.Resource.Designer.Resource.Id
                .progressBar);
        if (progressBar != null && progressBarColor != null) {
            try {
                progressBar.IndeterminateTintList = ColorStateList.ValueOf(progressBarColor.Value);
            }
            catch {
                /* ignore */
            }
        }
    }

    private static string GetChromeVersionFromUserAgent(string? userAgent)
    {
        if (userAgent == null)
            throw new ArgumentNullException(nameof(userAgent));

        var parts = userAgent.Split("Chrome/");
        if (parts.Length < 2)
            throw new ArgumentException("Could not extract Chrome version from user agent.");

        return parts[1].Split(' ').First();
    }

    private static int GetWebViewVersion(WebView webView)
    {
        var versionName = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? WebView.CurrentWebViewPackage?.VersionName
            : GetChromeVersionFromUserAgent(webView.Settings.UserAgentString);

        var parts = versionName?.Split('.');
        return parts?.Length > 0 ? int.Parse(parts[0]) : 0;
    }

    private string GetLaunchUrl(WebView webView)
    {
        var mainUrl = $"{VpnHoodAppWebServer.Instance.Url}?nocache={VpnHoodAppWebServer.Instance.SpaHash}";
        if (GetWebViewVersion(webView) >= options.WebViewRequiredVersion || options.WebViewUpgradeUrl == null)
            return mainUrl;

        var upgradeUrl = options.WebViewUpgradeUrl.IsAbsoluteUri
            ? options.WebViewUpgradeUrl
            : new Uri(VpnHoodAppWebServer.Instance.Url, options.WebViewUpgradeUrl);

        return upgradeUrl.ToString();
    }

    private void InitWebUi()
    {
        try {
            WebView = new WebView(ActivityEvent.Activity);
            WebView.Settings.JavaScriptEnabled = true;
            WebView.Settings.DomStorageEnabled = true;
            WebView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
            WebView.Settings.SetSupportMultipleWindows(true);
            // WebView.SetLayerType(LayerType.Hardware, null); // it may cause poor performance if forced
            if (VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor != null)
                WebView.SetBackgroundColor(VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor.Value
                    .ToAndroidColor());

            var webViewClient = new AndroidAppWebViewClient();
            webViewClient.PageLoaded += WebViewClient_PageLoaded;
            WebView.SetWebViewClient(webViewClient);
            WebView.SetWebChromeClient(new AndroidAppWebChromeClient());
            if (VpnHoodApp.Instance.Features.IsDebugMode)
                WebView.SetWebContentsDebuggingEnabled(true);

            WebView.LoadUrl(GetLaunchUrl(WebView));
        }
        catch (Exception ex) {
            WebViewCreateException = ex;
            WebViewUpdaterPage.InitPage(ActivityEvent.Activity, ex);
        }
    }

    private void WebViewClient_PageLoaded(object? sender, EventArgs e)
    {
        if (_isWeViewVisible) return; // prevent double set SetContentView
        if (WebView == null) throw new Exception("WebView has not been loaded yet!");
        ActivityEvent.Activity.SetContentView(WebView);
        _isWeViewVisible = true;

        if (VpnHoodApp.Instance.Resource.Colors.NavigationBarColor != null)
            ActivityEvent.Activity.Window?.SetNavigationBarColor(VpnHoodApp.Instance.Resource.Colors.NavigationBarColor
                .Value.ToAndroidColor());
    }

    protected override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && WebView?.CanGoBack() == true) {
            WebView.GoBack();
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }
}