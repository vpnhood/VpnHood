
namespace VpnHood.AppLib.Api.HttpClients;

// Makes the app's API for a UI that runs somewhere other than the app: a paired browser, dialing
// the web server it was served by. The six clients assembled here satisfy the same six interfaces
// the in-process API hands over (VpnHoodApp.Api), so a UI cannot tell which one it was given. The
// client's base address is the app's address, and its cookie jar - the browser's own - carries the
// pairing on every call.
//
// A factory rather than a type: what it returns IS a VpnHoodApi, which is sealed, so there is no
// HTTP flavour of it to name - only two ways to arrive at one.
public static class VpnHoodApiHttpFactory
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
