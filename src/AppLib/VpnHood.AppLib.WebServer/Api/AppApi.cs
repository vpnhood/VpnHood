namespace VpnHood.AppLib.WebServer.Api;

// The app's API as one object: the six controllers of the web server, whichever way a UI reaches
// them - in the app's own process, where they are the controllers themselves and no listener is
// involved (InProcessAppApi), or over HTTP from a paired browser (HttpAppApi). A UI written
// against this runs on the device and in a browser alike, and is the same UI in both.
public sealed class AppApi(
    IAppController app,
    IClientProfileController clientProfiles,
    IAccountController account,
    IBillingController billing,
    IIntentController intents,
    IProxyEndPointController proxyEndPoints)
{
    public IAppController App { get; } = app;
    public IClientProfileController ClientProfiles { get; } = clientProfiles;
    public IAccountController Account { get; } = account;
    public IBillingController Billing { get; } = billing;
    public IIntentController Intents { get; } = intents;
    public IProxyEndPointController ProxyEndPoints { get; } = proxyEndPoints;
}
