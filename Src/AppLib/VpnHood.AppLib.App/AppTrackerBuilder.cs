using Ga4.Trackers;

namespace VpnHood.AppLib;

public static class AppTrackerBuilder
{
    private static TrackEvent BuildAdFailed(string eventName, string? adNetwork, string errorMessage, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) countryCode = "(vh_unknown)";
        if (string.IsNullOrWhiteSpace(errorMessage)) errorMessage = "(vh_unknown)";

        return new TrackEvent {
            EventName = eventName,
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork ?? "(vh_unknown)" },
                { "error", errorMessage },
                { "country", countryCode }
            }
        };
    }
    public static TrackEvent BuildAdFailed(string? adNetwork, string errorMessage, string? countryCode)
    {
        return BuildAdFailed(eventName: "vh_ad_failed", adNetwork: adNetwork, errorMessage: errorMessage, countryCode: countryCode);
    }

    public static TrackEvent BuildLoadAdFailed(string adNetwork, string errorMessage, string? countryCode)
    {
        return BuildAdFailed(eventName: "vh_ad_load_failed", adNetwork: adNetwork, errorMessage: errorMessage, countryCode: countryCode);
    }

    public static TrackEvent BuildShowAdFailed(string? adNetwork, string errorMessage, string? countryCode)
    {
        return BuildAdFailed(eventName: "vh_ad_show_failed", adNetwork: adNetwork, errorMessage: errorMessage, countryCode: countryCode);
    }

    public static TrackEvent BuildShowAdOk(string adNetwork, string? countryCode)
    {
        if (countryCode?.Trim() is null or "") countryCode = "(vh_unknown)";

        return new TrackEvent {
            EventName = "vh_ad_show_ok",
            Parameters = new Dictionary<string, object> {
                { "ad_network", adNetwork },
                { "country", countryCode },
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