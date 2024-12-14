namespace VpnHood.AppLibs.Droid.Common.Activities;

public class AndroidMainActivityWebViewOptions : AndroidMainActivityOptions
{
    public bool SpaListenToAllIps { get; init; }
    public int? SpaDefaultPort { get; init; }
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}