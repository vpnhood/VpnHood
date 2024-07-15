using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Properties;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config",  // required for localhost
    SupportsRtl = true, AllowBackup = true)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions CreateAppOptions() => new()
    {
        Resource = DefaultAppResource.Resource,
        AccessKeys = AssemblyInfo.IsDebugMode ? [ClientOptions.SampleAccessKey] : [],
        UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
        IsAddAccessKeySupported = true,
        UpdaterService = AssemblyInfo.CreateUpdaterService(),
        CultureService = AndroidAppAppCultureService.CreateIfSupported(),
        UiService = new AndroidAppUiService(),
        LogAnonymous = !AssemblyInfo.IsDebugMode
    };
}