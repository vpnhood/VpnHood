using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.SpaWebView;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    // Same SPA zip as Client, but the 'connect' branding theme (see SpaResourcesFactory).
    public static AppResources Resources => field ??= Create();

    private static AppResources Create()
    {
        var resources = SpaResourcesFactory.FromSpaZip(typeof(ConnectAppResources).Assembly, "VpnHood.App.Client.spa.zip", "connect");
        resources.IpLocationZipData = Ip2LocationLiteDb.ZipData;
        return resources;
    }

    public static AppFeature[] PremiumFeatures { get; } = [
        AppFeature.CustomDns,
        AppFeature.AlwaysOn,
        AppFeature.QuickLaunch,
        AppFeature.SplitIpViaApp,
        AppFeature.SplitIpViaDevice,
        AppFeature.SplitDomain
    ];
}