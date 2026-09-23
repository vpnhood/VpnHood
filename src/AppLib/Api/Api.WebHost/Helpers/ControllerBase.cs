namespace VpnHood.AppLib.Api.WebHost.Helpers;

// An adapter from HTTP onto the API, as an ASP.NET controller is: it holds the contract interface,
// binds a request to a call and writes the reply. The API it calls knows nothing of any of this -
// the same object answers a UI in the app's own process, where there is no request to bind.
public abstract class ControllerBase
{
    public abstract void AddRoutes(IRouteMapper mapper);
}
