using System.Globalization;
using Android.Runtime;
using Com.Appsflyer;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Toolkit.Logging;


namespace VpnHood.App.Connect.Droid.Web;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("CHANNEL", Value = "GitHub")]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // initialize the app flyer
        if (!string.IsNullOrEmpty(appConfigs.AppsFlyerDevKey))
            InitAppsFlyer(appConfigs.AppsFlyerDevKey, useRegionPolicy: !AppConfigs.IsDebugMode);

        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(appId: PackageName!, "VpnHoodConnect", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resources = resources,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            AdjustForSystemBars = false
        };
    }

    private void InitAppsFlyer(string appsFlyerDevKey, bool useRegionPolicy)
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
            AppsFlyerLib.Instance.Init(appsFlyerDevKey, null, this);
            AppsFlyerLib.Instance.Start(this);

        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "AppsFlyer initialization failed.");
        }
    }
}