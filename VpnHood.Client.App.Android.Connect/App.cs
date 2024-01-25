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
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : AndroidApp(javaReference, transfer)
{
    protected override AppOptions AppOptions => new()
    {
        Resources = VpnHoodAppResource.Resources,
        UpdateInfoUrl = AssemblyInfo.UpdateInfoUrl
    };

    public static HttpClient StoreHttpClient => StoreHttpClientLazy.Value;
    private static readonly Lazy<HttpClient> StoreHttpClientLazy = new(() =>
    {
        var handler = new HttpClientHandler();
        if (AssemblyInfo.IsDebugMode)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        return new HttpClient(handler) { BaseAddress = AssemblyInfo.StoreBaseUri };
    });
}

