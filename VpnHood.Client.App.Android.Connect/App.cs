using System.Drawing;
using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid.Connect;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    NetworkSecurityConfig = "@xml/network_security_config",  // required for localhost
    SupportsRtl = true, AllowBackup = true)]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions AppOptions
    {
        get
        {
            var resources = VpnHoodAppResource.Resources;
            resources.Colors.NavigationBarColor = Color.Red;
            resources.Colors.WindowBackgroundColor = Color.GreenYellow;
            return new()
            {
                Resources = VpnHoodAppResource.Resources,
                UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
                UiName = "VpnHoodConnect",
                IsAddServerSupported = false
            };
        }
    }
}

