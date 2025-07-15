using Ga4.Trackers;

namespace VpnHood.AppLib;

public static class AppTrackerBuilder
{
    public static TrackEvent BuildLoadAdFailed(string adNetwork, string errorMessage, string? countryCode)
    {
        return new TrackEvent {
            EventName = "vh_ad_load_failed",
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork },
                { "error", errorMessage },
                { "country", countryCode ?? "(vh_unknown)" }
            }
        };
    }

    public static TrackEvent BuildShowAdFailed(string? adNetwork, string errorMessage, string? countryCode)
    {
        return new TrackEvent {
            EventName = "vh_ad_show_failed",
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork ?? "(vh_unknown)" },
                { "error", errorMessage },
                { "country", countryCode ?? "(vh_unknown)" }
            }
        };
    }

    public static TrackEvent BuildShowAdOk(string adNetwork, string? countryCode)
    {
        if (countryCode?.Trim() == string.Empty)
            countryCode = "empty";
        
        return new TrackEvent {
            EventName = "vh_ad_show_ok",
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork },
                { "country", countryCode ?? "(vh_unknown)" },
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