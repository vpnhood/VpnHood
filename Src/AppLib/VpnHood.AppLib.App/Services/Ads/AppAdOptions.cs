using System.Diagnostics;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdOptions
{
    public TimeSpan ShowAdPostDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan LoadAdPostDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LoadAdTimeout { get; set; } = Debugger.IsAttached 
        ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30);
    public bool PreloadAd { get; set; }
}