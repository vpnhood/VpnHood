namespace VpnHood.AppLib.Api.InProcessHost;

// The app's API for a UI in the app's own process: the contract answered against the app itself,
// called directly. No listener, no port and no JSON - a head that shows a native UI needs none of
// them, and this assembly has no web server in it to make them. A web host serves the very same
// object over HTTP, so neither transport can drift from the other. Pairing is the one call that
// needs a listener, so the head that runs one hands it in; the rest work without it.
public static class InProcessVpnHoodApi
{
    public static VpnHoodApi Create(VpnHoodApp app, Func<IRemoteAccessHost>? remoteAccessHostProvider = null)
    {
        return new VpnHoodApi(
            app: new AppApi(app, remoteAccessHostProvider),
            clientProfiles: new ClientProfilesApi(app),
            account: new AccountApi(app),
            billing: new BillingApi(app),
            intents: new IntentsApi(app),
            proxyEndPoints: new ProxyEndPointsApi(app));
    }
}
