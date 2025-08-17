using Android.Runtime;
using VpnHood.AppLib;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Services.Updaters;

namespace VpnHood.App.Client.Droid.Web;

[Application(
    Label = AppConfigs.AppName,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        var resources = ClientAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;

        return new AppOptions(PackageName!, "VpnHood", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            Resources = resources,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            IsLocalNetworkSupported = true,
            AdjustForSystemBars = false,
            AllowEndPointStrategy = true,
            AllowRecommendUserReviewByServer = false,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdateDelay = TimeSpan.FromDays(1)
            },
        };
    }
}