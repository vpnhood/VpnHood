using System.Drawing;
using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Common.Constants;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Connect.Web;

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
    public static int? SpaDefaultPort => IsDebugMode ? 9581 : 9580;
    public static bool SpaListenToAllIps => IsDebugMode; // if true it will cause crash in network change

    protected override AppOptions CreateAppOptions()
    {
        // load app settings and resources
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = "VpnHood! CONNECT";
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        var appConfigs = AppConfigs.Load();
        return new AppOptions(appId: PackageName!, IsDebugMode) {
            DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resource = resources,
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood.Client.App.Connect/releases/latest/download/VpnHoodConnect-android-web.json"),
            AllowEndPointTracker = true,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId
        };
    }

#if DEBUG
    public static bool IsDebugMode => true;
#else
    public static bool IsDebugMode => false;
#endif
}
