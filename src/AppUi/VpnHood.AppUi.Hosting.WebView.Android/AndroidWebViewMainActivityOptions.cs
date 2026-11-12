using VpnHood.AppLib.App.Android.Activities;

namespace VpnHood.AppUi.Hosting.WebView.Android;

/// <summary>Represents the web view UI's activity options: the app's, and the system web view's version check.</summary>
public class AndroidWebViewMainActivityOptions : AndroidMainActivityOptions
{
    /// <summary>
    /// Gets the page that asks the person to update an outdated system web view, relative to the app's web host or
    /// absolute, or <see langword="null"/> for none. The default is <c>/webview_upgrade/index.html</c>.
    /// </summary>
    /// <remarks>The page gets the current and the required versions in its query.</remarks>
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);

    /// <summary>Gets the least major version of the system web view the web UI needs. The default is 69.</summary>
    /// <remarks>A version below 50 is not checked.</remarks>
    public int WebViewRequiredVersion { get; init; } = 69;
}