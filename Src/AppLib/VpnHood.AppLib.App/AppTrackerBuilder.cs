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
                { "is_show", errorMessage is null },
                { "error", errorMessage ?? string.Empty }
            }
        };
    }

    public static TrackEvent BuildFirstLaunch(string deviceId, string countryCode)
    {
        return new TrackEvent {
            EventName = "vh_first_launch",
            Parameters = new Dictionary<string, object> {
                { "deviceId", deviceId },
                { "country", countryCode }
            }
        };
    }
}