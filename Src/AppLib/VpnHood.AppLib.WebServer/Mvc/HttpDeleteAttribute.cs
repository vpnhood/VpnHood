namespace VpnHood.AppLib.WebServer.Mvc;

public class HttpDeleteAttribute : RouteAttribute
{
    public HttpDeleteAttribute(string template = "") : base(template) { }
}