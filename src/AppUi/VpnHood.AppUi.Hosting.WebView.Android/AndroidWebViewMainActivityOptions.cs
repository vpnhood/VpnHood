using VpnHood.AppLib.App.Android.Activities;

namespace VpnHood.AppUi.Hosting.WebView.Android;

public class AndroidWebViewMainActivityOptions : AndroidMainActivityOptions
{
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}