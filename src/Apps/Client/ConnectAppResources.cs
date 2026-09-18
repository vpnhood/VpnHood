using System.Reflection;
using VpnHood.AppLib;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.Api.App;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    private const string SpaZipName = "VpnHood.App.Client.spa.zip";
    private const string AvaloniaBrowserZipName = "VpnHood.App.Client.avalonia-browser.zip";
    private static readonly Assembly Assembly = typeof(ConnectAppResources).Assembly;

    // Same SPA zip as Client, but the 'connect' branding theme (see SpaResourcesFactory).
    public static AppResources Resources => field ??= SpaResourcesFactory.FromSpaZip(SpaZip, "connect");

    // Not a UI resource: the ~14 MB IP-location database the engine reads for country splits and
    // location lookups. Lazy, so a run that never asks for a country never materializes it.
    public static Lazy<byte[]> IpLocationZipData { get; } = new(() => Ip2LocationLiteDb.ZipData);

    // The web root VpnHoodAppWebHost serves, one per head: the SPA; or, for the Avalonia UI, its
    // browser build for a paired phone, when this build embeds one (see the project file).
    public static ReadOnlyMemory<byte> GetWebRootZip(bool avaloniaUi)
    {
        return avaloniaUi
            ? EmbeddedResource.TryRead(Assembly, AvaloniaBrowserZipName) ?? SpaZip
            : SpaZip;
    }

    private static byte[] SpaZip => field ??= EmbeddedResource.TryRead(Assembly, SpaZipName)
        ?? throw new InvalidOperationException($"The embedded SPA bundle '{SpaZipName}' was not found: neither the ClassicSpa package nor the use-local-spa embed supplied spa.zip.");

    public static AppFeature[] PremiumFeatures { get; } = [
        AppFeature.CustomDns,
        AppFeature.AlwaysOn,
        AppFeature.QuickLaunch,
        AppFeature.SplitIpViaApp,
        AppFeature.SplitIpViaDevice,
        AppFeature.SplitDomain
    ];
}
