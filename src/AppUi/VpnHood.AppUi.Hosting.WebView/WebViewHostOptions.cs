namespace VpnHood.AppUi.Hosting.WebView;

public class WebViewHostOptions
{
    // Optional transform of the computed launch URL. Android uses this to redirect to its
    // "please update your WebView" page when the installed system WebView is too old.
    public Func<Uri, Uri>? LaunchUrlBuilder { get; set; }
}
