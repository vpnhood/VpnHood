namespace VpnHood.AppLibs;

public class AppAdOptions
{
    public TimeSpan ShowAdPostDelay { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan LoadAdPostDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan LoadAdTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public bool PreloadAd { get; init; }
}