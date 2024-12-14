using Android.Runtime;
using VpnHood.AppLibs;
using VpnHood.AppLibs.Droid.Common;
using VpnHood.AppLibs.Droid.Common.Constants;
using VpnHood.AppLibs.Resources;

namespace VpnHood.Apps.Client.Droid.Web;

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
        var appConfigs = AppConfigs.Load();

        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = appConfigs.AppName;

        return new AppOptions(PackageName!, AppConfigs.IsDebugMode) {
            StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"), // for compatibility with old VpnHood app versions to keep tokens
            Resource = resources,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            IsAddAccessKeySupported = true
        };
    }
}