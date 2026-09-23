using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdOptions
{
    public TimeSpan ShowAdPostDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan LoadAdPostDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan LoadAdTimeout { get; set; } = TimeSpan.FromSeconds(30).WhenNoDebugger();
    public TimeSpan ExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);
    public bool PreloadAd { get; set; }
    public bool RejectAdBlocker { get; set; }
    public string[]? AllowedPrivateDnsProviders { get; set; }
}