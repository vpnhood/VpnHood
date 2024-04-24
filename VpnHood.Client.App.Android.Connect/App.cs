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

[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = "ca-app-pub-8662231806304184~1740102860")]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    protected override AppOptions AppOptions
    {
        get
        {
            var resources = DefaultAppResource.Resource;
            resources.Colors.NavigationBarColor = Color.FromArgb(100, 32, 25, 81);
            resources.Colors.WindowBackgroundColor = Color.FromArgb(100, 32, 25, 81);
            return new AppOptions
            {
                AccessKeys = [AssemblyInfo.GlobalServersAccessKey], 
                Resource = DefaultAppResource.Resource,
                UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
                UiName = "VpnHoodConnect",
                IsAddServerSupported = false
            };
        }
    }
}

