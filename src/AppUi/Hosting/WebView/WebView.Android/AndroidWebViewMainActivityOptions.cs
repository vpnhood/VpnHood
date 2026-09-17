using VpnHood.AppLib.Droid.Common.Activities;

namespace VpnHood.AppUi.Hosting.WebView.Droid;

public class AndroidWebViewMainActivityOptions : AndroidMainActivityOptions
{
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}