namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidMainActivityWebViewOptions : AndroidMainActivityOptions
{
    public bool ListenToAllIps { get; init; }
    public int? DefaultSpaPort { get; init; }
    public Uri? WebViewUpgradeUrl { get; init; } = new("/webview_upgrade/index.html", UriKind.Relative);
    public int WebViewRequiredVersion { get; init; } = 69;
}