using System.Drawing;
using Android.Runtime;
using VpnHood.AppLib;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.Droid.Common;
using VpnHood.AppLib.Droid.Common.Constants;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.App.Connect.Droid.Web;

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
        // load app settings and resources
        var appConfigs = AppConfigs.Load();

        var resources = DefaultAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        return new AppOptions(appId: PackageName!, "VpnHoodConnect", AppConfigs.IsDebugMode) {
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
}