namespace VpnHood.AppLib.WebServer;

public abstract class ControllerBase
{
    public abstract void AddRoutes(IRouteMapper mapper);
}