using System.Globalization;
using Android.Content;
using Com.Appsflyer;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Connect.Droid.Web;

public static class AppFlyerUtils
{
    public static void InitAppsFlyer(Context context, string appsFlyerDevKey, bool useRegionPolicy)
    {
        try {
            // Start AppsFlyer if the user's country is China
            if (useRegionPolicy && !RegionInfo.CurrentRegion.Name.Equals("CN", StringComparison.OrdinalIgnoreCase)) {
                VhLogger.Instance.LogInformation("Bypassing AppsFlyer due to regional policy. {DeviceRegion}", RegionInfo.CurrentRegion.Name);
                return;
            }

            VhLogger.Instance.LogInformation("Initialize AppsFlyer. DeviceRegion: {DeviceRegion}", RegionInfo.CurrentRegion.Name);
            AppsFlyerLib.Instance.SetDebugLog(AppConfigs.IsDebugMode);
            AppsFlyerLib.Instance.SetDisableAdvertisingIdentifiers(true);
            AppsFlyerLib.Instance.Init(appsFlyerDevKey, null, context);
            AppsFlyerLib.Instance.Start(context);

        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "AppsFlyer initialization failed.");
        }
    }
}