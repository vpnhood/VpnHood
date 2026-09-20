using Avalonia;
using Avalonia.Browser;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.AvaloniaUI.Browser;

// The Avalonia UI in a browser: the page the app's web server hands a paired device, served by the
// app itself. Nothing of the app runs here. The API is dialed over HTTP at the address the page
// came from, the browser's own cookie carrying the pairing, and the files the UI draws from -
// images, flags, fonts, words, documents - are fetched from the same server by name, as they are
// asked for: the app's web host serves its own UI's store at /assets/, and this page reads it
// through the same interface the app's own UI reads its folder through.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // main.js passes the page's address; the API and the assets are relative to its origin
        var pageUrl = new Uri(args.Length > 0 ? args[0] : "http://localhost:9090/");
        var http = new HttpClient { BaseAddress = new Uri(pageUrl.GetLeftPart(UriPartial.Authority) + "/") };

        await AppModel.Init(HttpVpnHoodApi.Create(http), CancellationToken.None);
        await ClassicAvaloniaApp.PrepareContentAsync(new HttpAssetProvider(http, "assets/"), CancellationToken.None);
        await AppModel.Configure(ClassicAvaloniaApp.AvailableCultures, CancellationToken.None);
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<ClassicAvaloniaApp>();
    }
}
