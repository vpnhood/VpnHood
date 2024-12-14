using System.Drawing;
using Android.Runtime;
using VpnHood.AppLibs;
using VpnHood.AppLibs.Droid.Common;
using VpnHood.AppLibs.Droid.Common.Constants;
using VpnHood.AppLibs.Resources;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.Apps.Connect.Droid.Web;

[Application(
    Label = AndroidAppConstants.Label,
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
        
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = appConfigs.AppName;
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        return new AppOptions(appId: PackageName!, AppConfigs.IsDebugMode) {
            DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resource = resources,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId
        };
    }

}
