using System.Reflection;
using VpnHood.AppLib;

namespace VpnHood.App.Client;

public static class ClientAppResources
{
    private const string ResourcePrefix = "VpnHood.App.Client.";
    private const string SpaZipName = "spa.zip";
    private static readonly Assembly Assembly = typeof(ClientAppResources).Assembly;

    // Colors + system-tray icons come from the SPA zip's branding/default manifest (see
    // SpaResourcesFactory) — the SPA package owns the whole visual identity.
    public static AppResources Resources => field ??= SpaResourcesFactory.FromSpaZip(SpaZip);

    // In production embedded by the VpnHood.AppLib.Assets.ClassicSpa package's build targets,
    // locally by the use-local-spa embed.
    private static byte[] SpaZip => field ??= EmbeddedResource.TryRead(Assembly, ResourcePrefix + SpaZipName)
        ?? throw new InvalidOperationException($"The embedded SPA bundle '{SpaZipName}' was not found: neither the ClassicSpa package nor the use-local-spa embed supplied spa.zip.");
}
