namespace VpnHood.AppLib.SpaWebView;

public class SpaWebViewHostOptions
{
    // Optional transform of the computed launch URL. Android uses this to redirect to its
    // "please update your WebView" page when the installed system WebView is too old.
    public Func<Uri, Uri>? LaunchUrlBuilder { get; set; }

    // Message shown on the fatal error screen when the UI cannot be started or recovered.
    public string ServerNotRespondingMessage { get; set; } = "The user interface is not responding.";

    // How many automatic reload/restart attempts before giving up and showing the error screen.
    // Reset to zero whenever a page loads successfully.
    public int MaxRecoveryAttempts { get; set; } = 3;
}
