namespace VpnHood.Client.App;

public class AppAdOptions
{
    public TimeSpan ShowAdPostDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan LoadAdPostDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LoadAdTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public bool PreloadAd { get; set; }
}