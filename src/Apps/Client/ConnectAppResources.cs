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
        var assembly = typeof(ConnectAppResources).Assembly;
        var resources = SpaResourcesFactory.FromSpaZip(assembly, "VpnHood.App.Client.spa.zip", "connect");
        resources.IpLocationZipData = new Lazy<byte[]>(() => Ip2LocationLiteDb.ZipData);
        // the Avalonia UI's browser build, when this build embeds one (see the project file)
        resources.AvaloniaBrowserZipData = EmbeddedResource.TryRead(assembly, "VpnHood.App.Client.avalonia-browser.zip");
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
