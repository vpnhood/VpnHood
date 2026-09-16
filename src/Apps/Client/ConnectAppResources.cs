using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.SpaWebView;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    // Same SPA zip as Client, but the 'connect' branding theme (see SpaResourcesFactory).
    public static AppResources Resources => field ??= Create();

    // Not a UI resource: the ~14 MB IP-location database the engine reads for country splits and
    // location lookups. Lazy, so a run that never asks for a country never materializes it.
    public static Lazy<byte[]> IpLocationZipData { get; } = new(() => Ip2LocationLiteDb.ZipData);

    private static AppResources Create()
    {
        var assembly = typeof(ConnectAppResources).Assembly;
        var resources = SpaResourcesFactory.FromSpaZip(assembly, "VpnHood.App.Client.spa.zip", "connect");
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
