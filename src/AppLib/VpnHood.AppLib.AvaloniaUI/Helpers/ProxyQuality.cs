using VpnHood.AppLib.Assets;
using VpnHood.Core.Proxies.Management.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Helpers;

// The web UI's getStatusQualityDisplay: a proxy's quality as a word and the theme brush it is
// shown in; no brush for a quality nothing is known about.
internal static class ProxyQuality
{
    public static (string Text, string? BrushKey) Display(StatusQuality? quality)
    {
        var s = Strings.Current;
        return quality switch {
            StatusQuality.Excellent => (s.Excellent, "EnablePremiumBrush"),
            StatusQuality.Good => (s.Good, "GoodBrush"),
            StatusQuality.Fair => (s.Fair, "WarningBrush"),
            StatusQuality.Poor => (s.Poor, "DisablePremiumBrush"),
            StatusQuality.VeryPoor => (s.VeryPoor, "VeryPoorBrush"),
            StatusQuality.Failed => (s.Failed, "ErrorBrush"),
            _ => (s.NoData, null)
        };
    }
}
