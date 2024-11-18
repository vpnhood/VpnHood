using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Properties;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config", // required for localhost
    SupportsRtl = true, AllowBackup = true)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions() => new(PackageName!) {
        DeviceId = AndroidUtil.GetDeviceId(this), //this will be hashed using AppId
        StorageFolderPath = AppOptions.BuildStorageFolderPath(appId: "VpnHood"), // for compatibility with old versions
        Resource = DefaultAppResource.Resource,
        AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
        UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
        IsAddAccessKeySupported = true,
        UpdaterProvider = AssemblyInfo.CreateUpdaterProvider(),
        CultureProvider = AndroidAppCultureProvider.CreateIfSupported(),
        UiProvider = new AndroidUiProvider(),
        LogAnonymous = !AssemblyInfo.IsDebugMode
    };
}