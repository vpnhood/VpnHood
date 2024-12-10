using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Common.Constants;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Droid.Properties;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid;

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
    public static int? DefaultSpaPort => AssemblyInfo.IsDebugMode ? 9581 : 9580;

    protected override AppOptions CreateAppOptions()
    {
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = AssemblyInfo.IsDebugMode ? "VpnHOOD! Client (DEBUG)" : "VpnHood! Client";

        return new AppOptions(PackageName!) {
            DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"), // for compatibility with old versions
            Resource = resources,
            AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json"),
            IsAddAccessKeySupported = true,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            LogAnonymous = !AssemblyInfo.IsDebugMode
        };
    }

    public static bool IsDebugMode {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}