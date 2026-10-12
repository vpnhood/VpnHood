using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.Ads;

public class AppAdOptions
{
    public TimeSpan ShowAdPostDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan LoadAdPostDelay { get; set; } = TimeSpan.Zero;
    // An interstitial's: where it fails, the person connects without it. A rewarded ad has no limit of
    // the app's, as the person asked to watch it; its network ends a stuck load by itself.
    public TimeSpan LoadAdTimeout { get; set; } = TimeSpan.FromSeconds(30).WhenNoDebugger();
    public TimeSpan ExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);
    public bool PreloadAd { get; set; }
    public bool RejectAdBlocker { get; set; }
    public string[]? AllowedPrivateDnsProviders { get; set; }

    // the ad network's consent: its form before an ad's timed load, never with a preload, and the privacy
    // settings' way back to it; none where no network asks
    public IAdConsentProvider? ConsentProvider { get; set; }
}