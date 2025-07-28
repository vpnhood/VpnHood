using Android.Runtime;
using VpnHood.AppLib;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.AppLib.Droid.GooglePlay;

namespace VpnHood.App.Client.Droid.Google;

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
            Resources = resources,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            IsAddAccessKeySupported = true,
            IsLocalNetworkSupported = true,
            AdjustForSystemBars = false,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            AllowEndPointStrategy = true
        };
    }
}