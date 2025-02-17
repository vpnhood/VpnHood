using Ga4.Trackers;

namespace VpnHood.AppLib;

public static class AppTrackerBuilder
{
    public static TrackEvent BuildShowAdStatus(string adNetwork, string? errorMessage = null)
    {
        return new TrackEvent {
            EventName = "vh_ad_status",
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork },
                { "is_show", errorMessage != null },
                { "error", errorMessage ?? string.Empty }
            }
        };
    }
}