using System.Web;
using Android.Webkit;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.SpaWebView;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.Droid.ActivityEvents;
using VpnHood.Core.Client.Devices.Droid.Utils;
using Uri = System.Uri;

namespace VpnHood.AppLib.Droid.Common;

// Android ISpaWebView adapter: the only Android-specific SPA-hosting code. It owns the Android
// WebView, swapping the activity's content between the loading screen and the WebView, and maps the
// WebView client callbacks onto the platform-neutral SpaWebViewHost events. The WebView-version
// "please update" redirect is Android-specific, so it lives here (it needs the live WebView).
public sealed class AndroidSpaWebView : ISpaWebView
{
    private readonly IActivityEvent _activityEvent;
    private readonly AndroidMainActivityWebViewOptions _options;
    private WebView? _webView;
    private bool _webViewShown;

    private Activity Activity => _activityEvent.Activity;

    public event EventHandler? PageLoaded;
    // Android relies on the server health monitor + resume signal + Restarted reload for recovery, so
    // these two are declared for the interface but not raised here.
    public event EventHandler<SpaLoadFailedEventArgs>? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public AndroidSpaWebView(IActivityEvent activityEvent, AndroidMainActivityWebViewOptions options)
    {
        _activityEvent = activityEvent;
        _options = options;

        // Loading screen shown until the SPA first renders (swapped out in SetLoading(false)).
        AndroidAppLoader.Init(Activity);
    }

    public void Initialize()
    {
        _webView = new WebView(Activity);
        _webView.Settings.JavaScriptEnabled = true;
        _webView.Settings.DomStorageEnabled = true;
        _webView.Settings.MediaPlaybackRequiresUserGesture = false;
        _webView.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        _webView.Settings.SetSupportMultipleWindows(true);
        if (VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor != null)
            _webView.SetBackgroundColor(VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor.Value
                .ToAndroidColor());

        var webViewClient = new AndroidAppWebViewClient();
        webViewClient.PageLoaded += (_, _) => PageLoaded?.Invoke(this, EventArgs.Empty);
        _webView.SetWebViewClient(webViewClient);
        _webView.SetWebChromeClient(new AndroidAppWebChromeClient());
        if (VpnHoodApp.Instance.Features.IsDebugMode)
            WebView.SetWebContentsDebuggingEnabled(true);
    }

    public void Load(Uri url)
    {
        _webView?.LoadUrl(ResolveUrl(url).ToString());
    }

    public void Reload()
    {
        _webView?.Reload();
    }

    public void SetLoading(bool isLoading)
    {
        // Android swaps the whole content view rather than overlaying a spinner: the loading screen is
        // already shown (constructor / previous state); when loading finishes we swap to the WebView.
        if (!isLoading)
            ShowWebView();
    }

    public void ShowError(string message)
    {
        // If the WebView itself never came up, guide the user to update Android System WebView;
        // otherwise it's a server-side failure.
        if (_webView == null)
            WebViewUpdaterPage.ShowWebViewExceptionPage(Activity, new Exception(message));
        else
            WebViewUpdaterPage.ShowServerExceptionPage(Activity, new Exception(message));
    }

    public void Post(Action action)
    {
        AndroidUtils.RunOnUiThread(Activity, action);
    }

    // --- Android-specific lifecycle helpers the activity handler forwards to ---

    public void OnActivityPause() => _webView?.OnPause();
    public void OnActivityResume() => _webView?.OnResume();
    public bool CanGoBack() => _webView?.CanGoBack() == true;
    public void GoBack() => _webView?.GoBack();

    private void ShowWebView()
    {
        if (_webViewShown || _webView == null)
            return;

        Activity.SetContentView(_webView);
        _webViewShown = true;

        // Compatibility for Android 9 and below to set the navigation-bar color.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) &&
            VpnHoodApp.Instance.Resources.Colors.NavigationBarColor != null)
            Activity.Window?.SetNavigationBarColor(
                VpnHoodApp.Instance.Resources.Colors.NavigationBarColor.Value.ToAndroidColor());
    }

    // The bundled SPA is served from VpnHoodAppWebServer; if the installed system WebView is older
    // than the SPA needs, redirect to the upgrade page instead (mirrors the previous GetLaunchUrl).
    private Uri ResolveUrl(Uri mainUrl)
    {
        if (_webView == null)
            return mainUrl;

        var currentVersion = GetWebViewVersion(_webView);
        if (currentVersion >= _options.WebViewRequiredVersion ||
            currentVersion < 50 || // ignore OSes with a wrong version report such as HarmonyOS
            _options.WebViewUpgradeUrl == null)
            return mainUrl;

        var upgradeUrl = _options.WebViewUpgradeUrl.IsAbsoluteUri
            ? _options.WebViewUpgradeUrl
            : new Uri(VpnHoodAppWebServer.Instance.Url, _options.WebViewUpgradeUrl);

        var uriBuilder = new UriBuilder(upgradeUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["current-version"] = currentVersion.ToString();
        query["required-version"] = _options.WebViewRequiredVersion.ToString();
        uriBuilder.Query = query.ToString();
        return uriBuilder.Uri;
    }

    // Returns 0 when the version can't be determined so ResolveUrl treats it as "don't redirect"
    // (0 < 50) rather than throwing on the load path (which runs outside a try in some callers).
    private static int GetWebViewVersion(WebView webView)
    {
        try {
            var versionName = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? WebView.CurrentWebViewPackage?.VersionName
                : null;

            if (string.IsNullOrWhiteSpace(versionName))
                versionName = GetChromeVersionFromUserAgent(webView.Settings.UserAgentString);

            var parts = versionName.Split('.');
            return parts.Length > 0 ? int.Parse(parts[0]) : 0;
        }
        catch {
            return 0;
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
}
