using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Browser;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// Which UI is the page project's one decision, made as the desktop and Android hosts make it: a
// type argument, so the UI it does not name is not in the bundle. The page's Main is one line.
/// <summary>
/// Provides the Avalonia UI in a browser: the page the app's web host hands a paired device, served by the app itself.
/// </summary>
/// <remarks>
/// Nothing of the app runs here: the API is dialed over HTTP at the address the page came from, with the token that
/// address carries on the device or, on a paired phone, the browser's own cookie that carries the pairing; the UI's files
/// are fetched from the same server, under <c>/assets/</c>, as they are asked for.
/// </remarks>
public static class AvaloniaBrowserHost
{
    /// <summary>
    /// Runs the UI in the page: hands it the app's API over HTTP and its content from the server, then starts it.
    /// </summary>
    /// <typeparam name="TUi">The UI's application.</typeparam>
    /// <param name="args">
    /// The page's arguments: its address first, as <c>main.js</c> passes it; without one, <c>http://localhost:9090/</c>.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests while the UI starts.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// The UI's English words read as JSON <c>null</c>, or the host's own script is missing from its assembly.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">A file of the UI's content the UI needs cannot be read.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Assets.AssetNotFoundException">The UI's content has no file the UI needs.</exception>
    /// <exception cref="UriFormatException">The page's address is not valid.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The app failed with an exception of a type the transport does not rebuild, answered with an error it does not name,
    /// or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">
    /// The app's web host could not be reached, or answered a request for content with a failure status other than 404.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or an HTTP request timed out.
    /// </exception>
    /// <remarks>
    /// The page must hold an element with the ID <c>out</c>, which the UI draws in. A canceled token stops the start until
    /// Avalonia starts; Avalonia's own start takes none.
    /// </remarks>
    public static async Task RunAsync<TUi>(string[] args, CancellationToken cancellationToken)
        where TUi : Application, IAvaloniaUi, new()
    {
        // main.js passes the page's address; the API and the assets are relative to its origin
        var pageUrl = new Uri(args.Length > 0 ? args[0] : "http://localhost:9090/");
        var http = new HttpClient { BaseAddress = new Uri(pageUrl.GetLeftPart(UriPartial.Authority) + "/") };
        if (LocalApiToken.Read(pageUrl) is { } token)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await AvaloniaUiHosting.StartAsync<TUi>(VpnHoodApiHttpFactory.Create(http),
            new HttpAssetProvider(http, "assets/"), cancellationToken);
        await BrowserTextInput.RegisterAsync(cancellationToken);
        await BrowserExport.RegisterAsync(cancellationToken);
        await AppBuilder.Configure<TUi>().StartBrowserAppAsync("out");
    }
}
