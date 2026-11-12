namespace VpnHood.AppUi.Hosting.WebView;

/// <summary>Represents the options of a <see cref="WebViewHost"/>.</summary>
public class WebViewHostOptions
{
    /// <summary>Gets or sets a change to the launch address, or <see langword="null"/> for none.</summary>
    public Func<Uri, Uri>? LaunchUrlBuilder { get; set; }
}
