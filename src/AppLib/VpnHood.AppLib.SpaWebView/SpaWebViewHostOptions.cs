namespace VpnHood.AppLib.SpaWebView;

public class SpaWebViewHostOptions
{
    // Optional transform of the computed launch URL. Android uses this to redirect to its
    // "please update your WebView" page when the installed system WebView is too old.
    public Func<Uri, Uri>? LaunchUrlBuilder { get; set; }
}
