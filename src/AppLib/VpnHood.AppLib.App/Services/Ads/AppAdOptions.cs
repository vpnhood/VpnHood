using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.Ads;

/// <summary>Provides how the app loads and shows its ads (<see cref="AppOptions.AdOptions"/>).</summary>
public class AppAdOptions
{
    /// <summary>
    /// Gets or sets how long the app waits after an ad closes before it tells the VPN service the ad was shown; the
    /// state reads as connected for the last three seconds of it. The default is three seconds.
    /// </summary>
    public TimeSpan ShowAdPostDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets how long the app waits after it loads an ad, or finds one loaded, before it goes on. The default is
    /// none.
    /// </summary>
    public TimeSpan LoadAdPostDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets how long each network may take to load an interstitial before the next is tried; where none loads,
    /// the connect goes on without it, as long as a window is there to show it in and the ad-blocker check
    /// (<see cref="RejectAdBlocker"/>) does not fail it. The default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// A rewarded ad has no limit of the app's, as the person asked to watch it. While a debugger is attached,
    /// <see cref="VhUtils.DebuggerTimeout"/> takes its place where it is set.
    /// </remarks>
    public TimeSpan LoadAdTimeout { get; set; } = TimeSpan.FromSeconds(30).WhenNoDebugger();

    /// <summary>
    /// Gets or sets the time a session must have left, more than this, for a rewarded ad to extend it. The default is
    /// five minutes.
    /// </summary>
    public TimeSpan ExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a value indicating whether the app loads the next connect's interstitial ahead of it, when a window
    /// opens while the VPN is idle; a preload brings no consent form, and waits for the person's answer to one. The
    /// default is <see langword="false"/>.
    /// </summary>
    public bool PreloadAd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an ad that fails while the device's private DNS names a provider not in
    /// <see cref="AllowedPrivateDnsProviders"/> fails as an ad blocker's, even where the ad was not required. The
    /// default is <see langword="false"/>.
    /// </summary>
    public bool RejectAdBlocker { get; set; }

    /// <summary>
    /// Gets or sets the private DNS providers that do not count as an ad blocker (<see cref="RejectAdBlocker"/>), or
    /// <see langword="null"/>, and then none counts as one.
    /// </summary>
    public string[]? AllowedPrivateDnsProviders { get; set; }

    /// <summary>
    /// Gets or sets the ad network's consent, or <see langword="null"/> where no network asks for one: its form comes
    /// before an ad's timed load, never with a preload, and the privacy settings lead back to it.
    /// </summary>
    public IAdConsentProvider? ConsentProvider { get; set; }
}