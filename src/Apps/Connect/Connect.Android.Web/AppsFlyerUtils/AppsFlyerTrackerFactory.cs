using System.Globalization;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Android.Web.AppsFlyerUtils;

public class AppsFlyerTrackerFactory : ITrackerFactory
{
    public string? DevKey { get; init; }

    // The process's one AppsFlyer, set to the switch it is made with; none without a key, or outside
    // China, where our policy runs it.
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        if (string.IsNullOrEmpty(DevKey))
            return NullTrackerFactory.CreateNullTracker(createParams);

        var regionName = RegionInfo.CurrentRegion.Name;
        if (!regionName.Equals("CN", StringComparison.OrdinalIgnoreCase)) {
            VhLogger.Instance.LogInformation("AppsFlyer is off outside China. DeviceRegion: {DeviceRegion}", regionName);
            return NullTrackerFactory.CreateNullTracker(createParams);
        }

        var tracker = AppsFlyerTracker.GetInstance(DevKey);
        tracker.IsEnabled = createParams.IsEnabled;
        return tracker;
    }
}
