using VpnHood.AppLib.Droid.Common.Activities;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

public class AndroidSpaWebViewMainActivityOptions : AndroidMainActivityOptions
{
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}