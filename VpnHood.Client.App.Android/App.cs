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
    protected override AppOptions AppOptions => new()
    {
        Resources = VpnHoodAppResource.Resources,
        UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
        IsAddServerSupported = true
    };
}