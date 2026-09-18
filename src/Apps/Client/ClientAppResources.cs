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

    // The web root the app's web host serves - to its own web view and to a paired phone alike.
    // Which UI that is, is settled by what this build embedded (see the project file): the Avalonia
    // UI's browser build when it is there, otherwise the SPA.
    public static ReadOnlyMemory<byte> WebRootZip => WebRootZipData;

    private static byte[] WebRootZipData =>
        field ??= EmbeddedResource.TryRead(Assembly, AvaloniaBrowserZipName) ?? SpaZip;

    // In production embedded by the VpnHood.AppLib.Assets.ClassicSpa package's build targets,
    // locally by the use-local-spa embed.
    private static byte[] SpaZip => field ??= EmbeddedResource.TryRead(Assembly, SpaZipName)
        ?? throw new InvalidOperationException($"The embedded SPA bundle '{SpaZipName}' was not found: neither the ClassicSpa package nor the use-local-spa embed supplied spa.zip.");
}
