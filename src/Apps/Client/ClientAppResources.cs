using System.Reflection;
using VpnHood.AppLib;
using VpnHood.AppLib.Assets.Ip2LocationLite;

namespace VpnHood.App.Client;

public static class ClientAppResources
{
    private const string SpaZipName = "VpnHood.App.Client.spa.zip";
    private const string AvaloniaBrowserZipName = "VpnHood.App.Client.avalonia-browser.zip";
    private static readonly Assembly Assembly = typeof(ClientAppResources).Assembly;

    // Colors + system-tray icons come from the SPA zip's branding/default manifest (see
    // SpaResourcesFactory) — the SPA package owns the whole visual identity.
    public static AppResources Resources => field ??= SpaResourcesFactory.FromSpaZip(SpaZip);

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

    // In production embedded by the VpnHood.AppLib.Assets.ClassicSpa package's build targets,
    // locally by the use-local-spa embed.
    private static byte[] SpaZip => field ??= EmbeddedResource.TryRead(Assembly, SpaZipName)
        ?? throw new InvalidOperationException($"The embedded SPA bundle '{SpaZipName}' was not found: neither the ClassicSpa package nor the use-local-spa embed supplied spa.zip.");
}
