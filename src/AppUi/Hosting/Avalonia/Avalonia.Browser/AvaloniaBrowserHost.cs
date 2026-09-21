using Avalonia;
using Avalonia.Browser;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppUi.Common;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// The Avalonia UI in a browser: the page the app's web server hands a paired device, served by the
// app itself. Nothing of the app runs here. The API is dialed over HTTP at the address the page
// came from, the browser's own cookie carrying the pairing, and the files the UI draws from -
// images, flags, fonts, words, documents - are fetched from the same server by name, as they are
// asked for: the app's web host serves its own UI's store at /assets/, and this page reads it
// through the same interface the app's own UI reads its folder through.
//
// Which UI is the page project's one decision, made as the desktop and Android hosts make it: a
// type argument, so the UI it does not name is not in the bundle. The page's Main is one line.
public static class AvaloniaBrowserHost
{
    public static async Task RunAsync<TUi>(string[] args) where TUi : Application, IAvaloniaUi, new()
    {
        // main.js passes the page's address; the API and the assets are relative to its origin
        var pageUrl = new Uri(args.Length > 0 ? args[0] : "http://localhost:9090/");
        var http = new HttpClient { BaseAddress = new Uri(pageUrl.GetLeftPart(UriPartial.Authority) + "/") };

        await VhApp.Init(HttpVpnHoodApi.Create(http), CancellationToken.None);
        await TUi.PrepareContentAsync(new HttpAssetProvider(http, "assets/"), CancellationToken.None);
        await VhApp.Configure(TUi.AvailableCultures, CancellationToken.None);
        await AppBuilder.Configure<TUi>().StartBrowserAppAsync("out");
    }
}
