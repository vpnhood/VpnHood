using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.SpaWebView;

namespace VpnHood.App.Client;

public static class ClientAppResources
{
    // Colors + system-tray icons come from the SPA zip's branding/default manifest (see
    // SpaResourcesFactory) — the SPA package owns the whole visual identity.
    public static AppResources Resources => field ??= Create();

    private static AppResources Create()
    {
        var resources = SpaResourcesFactory.FromSpaZip(typeof(ClientAppResources).Assembly, "VpnHood.App.Client.spa.zip");
        resources.IpLocationZipData = Ip2LocationLiteDb.ZipData;
        return resources;
    }
}
