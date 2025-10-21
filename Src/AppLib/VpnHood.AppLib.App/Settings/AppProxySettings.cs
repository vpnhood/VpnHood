namespace VpnHood.AppLib.Settings;

public class AppProxySettings
{
    public AppProxyMode Mode { get; set; }
    public Uri? AutoUpdateListUrl { get; set; }
    public TimeSpan? AutoUpdateInterval { get; set; }
    public int AutoUpdateMinPenalty { get; set; } 
}