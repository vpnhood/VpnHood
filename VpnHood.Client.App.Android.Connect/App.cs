using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid.Connect;

[Application(
    Label = "@string/app_name",
    Icon = "@mipmap/appicon",
    Banner = "@mipmap/banner", // for TV
    UsesCleartextTraffic = true, // required for localhost
    SupportsRtl = true, AllowBackup = true)]
public class App : AndroidApp
{
    public App(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppOptions AppOptions => new()
    {
        UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl,
        Resources = VpnHoodAppResource.Resources,

    };

}


