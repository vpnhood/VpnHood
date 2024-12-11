using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Common.Constants;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid.Client.Web;

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
    public static bool SpaListenToAllIps => IsDebugMode;

    protected override AppOptions CreateAppOptions()
    {
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";

        return new AppOptions(PackageName!, IsDebugMode) {
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"), // for compatibility with old VpnHood app versions to keep tokens
            Resource = resources,
            AccessKeys = IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json"),
            IsAddAccessKeySupported = true,
            LogAnonymous = !IsDebugMode
        };
    }


#if DEBUG
    public static bool IsDebugMode => true;
#else
    public static bool IsDebugMode => false;
#endif

}