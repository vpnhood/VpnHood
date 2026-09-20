using System.Reflection;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.Client;

// The page the app's web host serves - to its own web view and to a paired phone alike: the Avalonia
// UI's browser build, which this assembly's project file embeds from AvaloniaUI.Browser's publish
// (_publish.ps1). The app extracts it under its storage, as it does the UI's own store.
//
// One page for both products: what a head shows of itself travels in AppOptions and the store's zip,
// not in the build served here.
public static class AppWebRoot
{
    private const string ResourcePrefix = "VpnHood.App.Client.";
    private const string ZipName = "avalonia-browser.zip";

    // A build that never published the browser UI embeds nothing, and the first request for the page
    // says which file is missing - rather than a head discovering at startup that it has half a UI.
    public static IAsset Zip { get; } = new Asset(
        new EmbeddedResourceAssetProvider(typeof(AppWebRoot).Assembly, ResourcePrefix), ZipName);
}
