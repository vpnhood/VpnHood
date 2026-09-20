using System.Reflection;
using VpnHood.AppLib;
using VpnHood.AppLib.Api.App;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    private const string ResourcePrefix = "VpnHood.App.Client.";
    private const string SpaZipName = "spa.zip";
    private static readonly Assembly Assembly = typeof(ConnectAppResources).Assembly;

    // Same SPA zip as Client, but the 'connect' branding theme (see SpaResourcesFactory).
    public static AppResources Resources => field ??= SpaResourcesFactory.FromSpaZip(SpaZip, "connect");

    private static byte[] SpaZip => field ??= EmbeddedResource.TryRead(Assembly, ResourcePrefix + SpaZipName)
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
