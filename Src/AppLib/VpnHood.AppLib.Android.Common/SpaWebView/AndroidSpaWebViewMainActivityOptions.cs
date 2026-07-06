namespace VpnHood.AppLib.Droid.Common.Activities;

public class AndroidMainActivityWebViewOptions : AndroidMainActivityOptions
{
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}