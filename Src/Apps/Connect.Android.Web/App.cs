using Android.Runtime;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.Device.Droid.Utils;


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
    : Application(javaReference, transfer)
{
    private AppOptions CreateAppOptions(AppConfigs appConfigs)
    {
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
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            AdjustForSystemBars = false,
            AllowRecommendUserReviewByServer = true,
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.FromDays(1)
            }
        };
    }

    public override void OnCreate()
    {
        var appConfigs = AppConfigs.Load();

        // initialize the app flyer
        if (!string.IsNullOrEmpty(appConfigs.AppsFlyerDevKey))
            AppFlyerUtils.InitAppsFlyer(this, appConfigs.AppsFlyerDevKey, useRegionPolicy: !AppConfigs.IsDebugMode);

        // initialize the app
        VpnHoodAndroidApp.Init(() => CreateAppOptions(appConfigs));

        base.OnCreate();
    }

    public override void OnTerminate()
    {
        if (VpnHoodAndroidApp.IsInit)
            VpnHoodAndroidApp.Instance.Dispose();
    }
}