using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Controllers;

namespace VpnHood.AppLib.WebServer;

// The app's API for a UI in the app's own process: the web server's controllers, called directly.
// No listener, no port and no JSON - a head that shows a native UI needs none of them until its
// pairing page asks for a phone. The three remote-access calls are the one place a web server is
// needed, and they reach the process's one through its singleton; bringing it up is the head's
// business, as it is today.
public static class InProcessAppApi
{
    public static AppApi Create(VpnHoodApp app)
    {
        return new AppApi(
            app: new AppController(app, () => VpnHoodAppWebServer.Instance),
            clientProfiles: new ClientProfileController(app),
            account: new AccountController(app),
            billing: new BillingController(app),
            intents: new IntentsController(app),
            proxyEndPoints: new ProxyEndPointController(app));
    }
}
