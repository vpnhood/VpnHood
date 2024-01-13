using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid;

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
        Resources = VpnHoodAppResource.Resources,
        UpdateInfoUrl = ApplicationContext?.PackageName == "com.vpnhood.client.android"
            ? new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json")
            : new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json")
    };
}