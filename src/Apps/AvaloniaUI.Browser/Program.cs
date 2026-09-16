using Avalonia;
using Avalonia.Browser;
using VpnHood.AppLib.Api.Clients;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi;

namespace VpnHood.App.AvaloniaUI.Browser;

// The Avalonia UI in a browser: the page the app's web server hands a paired device, served by the
// app itself. Nothing of the app runs here. The API is dialed over HTTP at the address the page
// came from, the browser's own cookie carrying the pairing, and the files the UI draws from -
// images, flags, fonts, documents - are fetched from the same server into the runtime's file
// system before the UI starts, so the UI reads them as it does on a device: from a folder. The
// words need no fetch: they are resources of the content assembly, which came with the page.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // main.js passes the page's address; the API and the assets are relative to its origin
        var pageUrl = new Uri(args.Length > 0 ? args[0] : "http://localhost:9090/");
        var http = new HttpClient { BaseAddress = new Uri(pageUrl.GetLeftPart(UriPartial.Authority) + "/") };

        await AppData.Init(HttpVpnHoodApi.Create(http), CancellationToken.None);
        var assetsFolderPath = await BrowserAssets.Download(http, CancellationToken.None);
        AppContent.FolderResolver = () => assetsFolderPath;
        ClassicAvaloniaApp.PrepareContent();
        await AppData.Configure(ClassicAvaloniaApp.AvailableCultures, CancellationToken.None);
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<ClassicAvaloniaApp>();
    }
}
