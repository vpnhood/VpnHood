using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Common.Constants;
using VpnHood.Client.App.Droid.Properties;
using VpnHood.Client.App.Resources;

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
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"), // for compatibility with old VpnHood app versions to keep tokens
            Resource = resources,
            AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json"),
            IsAddAccessKeySupported = true,
            LogAnonymous = !AssemblyInfo.IsDebugMode
        };
    }
}