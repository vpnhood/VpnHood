using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
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
        Resources = VpnHoodAppResource.Resources,
        UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android.json")
    };
}

