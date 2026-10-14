using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.WebView.Branding;

// What a branding manifest names (AppBranding); a colour it does not name is null, and that part of
// the chrome keeps the system's.
public class AppBrandingColors
{
    public VhColor? WindowBackgroundColor { get; init; }
    public VhColor? NavigationBarColor { get; init; }
    public VhColor? ProgressBarColor { get; init; }
}
