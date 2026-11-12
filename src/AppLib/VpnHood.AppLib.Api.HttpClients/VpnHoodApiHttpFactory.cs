
namespace VpnHood.AppLib.Api.HttpClients;

/// <summary>
/// Provides the app's API for a UI that runs somewhere other than the app, as a paired browser, over HTTP calls to the
/// app's web host.
/// </summary>
/// <remarks>
/// The clients it puts together answer the same six interfaces the app's in-process API does, so a UI cannot tell
/// which one it was given. A failure the app's web host reports comes back as the exception it names, as the in-process
/// call would have thrown it, and one this side cannot make as an <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException"/>.
/// </remarks>
public static class VpnHoodApiHttpFactory
{
    /// <summary>Creates the app's API over HTTP.</summary>
    /// <param name="httpClient">
    /// The HTTP client, whose base address is the app's web host and which carries the credential on every call: the
    /// local token as its bearer header on the device, the browser's cookie on a paired phone. The caller keeps and
    /// disposes it.
    /// </param>
    /// <returns>The app's API, whose calls go to the app over <paramref name="httpClient"/>.</returns>
    public static VpnHoodApi Create(HttpClient httpClient)
    {
        return new VpnHoodApi(
            app: new AppClient(httpClient),
            vpnProfiles: new VpnProfileClient(httpClient),
            account: new AccountClient(httpClient),
            billing: new BillingClient(httpClient),
            intents: new IntentClient(httpClient),
            proxyEndPoints: new ProxyEndPointClient(httpClient));
    }
}
