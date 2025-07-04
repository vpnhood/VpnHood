using Ga4.Trackers;

namespace VpnHood.AppLib;

public static class AppTrackerBuilder
{
    public static TrackEvent BuildShowAdStatus(string providerName, string? country, string? errorMessage)
    {
        return new TrackEvent {
            EventName = "vh_ad_status",
            Parameters = new Dictionary<string, object> {
                { "ad_network", providerName },
                { "is_show", errorMessage is null },
                { "error", errorMessage ?? string.Empty },
                { "country", country ?? "(not set)" },
            }
        };
    }

    public static TrackEvent BuildShowAdFailed(string errorMessage)
    {
        return new TrackEvent {
            EventName = "vh_ad_failed",
            Parameters = new Dictionary<string, object> {
                { "error", errorMessage }
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