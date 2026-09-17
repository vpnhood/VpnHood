using VpnHood.AppLib.Assets;

using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Sessions;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

// The web UI's getStatusQualityDisplay: a proxy's quality as a word and the theme brush it is
// shown in; no brush for a quality nothing is known about.
internal static class ProxyQuality
{
    // Traffic carries the two counts; the sum is the UI's arithmetic, not the contract's.
    public static long Total(this Traffic traffic)
    {
        return traffic.Sent + traffic.Received;
    }

    // The contract carries the counts; whether that means "tried yet" is this UI's reading of them.
    public static bool HasUsed(this ProxyEndPointStatus status)
    {
        return status.SucceededCount > 0 || status.FailedCount > 0;
    }

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
