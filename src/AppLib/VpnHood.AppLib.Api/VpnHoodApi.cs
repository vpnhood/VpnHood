namespace VpnHood.AppLib.Api;

// The app's API as one object: the six controllers of the web server, whichever way a UI reaches
// them - in the app's own process, where they are the controllers themselves and no listener is
// involved (InProcessVpnHoodApi), or over HTTP from a paired browser (HttpVpnHoodApi). A UI written
// against this runs on the device and in a browser alike, and is the same UI in both.
public sealed class VpnHoodApi(
    IAppApi app,
    IClientProfilesApi clientProfiles,
    IAccountApi account,
    IBillingApi billing,
    IIntentsApi intents,
    IProxyEndPointsApi proxyEndPoints)
{
    public IAppApi App { get; } = app;
    public IClientProfilesApi ClientProfiles { get; } = clientProfiles;
    public IAccountApi Account { get; } = account;
    public IBillingApi Billing { get; } = billing;
    public IIntentsApi Intents { get; } = intents;
    public IProxyEndPointsApi ProxyEndPoints { get; } = proxyEndPoints;
}
