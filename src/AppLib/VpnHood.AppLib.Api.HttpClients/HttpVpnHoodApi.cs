
namespace VpnHood.AppLib.Api.HttpClients;

// The app's API for a UI that runs somewhere else than the app: a paired browser, dialing the web
// server it was served by. The client's base address is that address, and its cookie jar - the
// browser's own - carries the pairing on every call.
public static class HttpVpnHoodApi
{
    public static VpnHoodApi Create(HttpClient httpClient)
    {
        return new VpnHoodApi(
            app: new AppClient(httpClient),
            clientProfiles: new ClientProfileClient(httpClient),
            account: new AccountClient(httpClient),
            billing: new BillingClient(httpClient),
            intents: new IntentClient(httpClient),
            proxyEndPoints: new ProxyEndPointClient(httpClient));
    }
}
