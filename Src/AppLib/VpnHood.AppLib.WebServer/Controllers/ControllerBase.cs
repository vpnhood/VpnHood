using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer.Controllers;

public abstract class ControllerBase
{
    public abstract void AddRoutes(IRouteMapper mapper);
}