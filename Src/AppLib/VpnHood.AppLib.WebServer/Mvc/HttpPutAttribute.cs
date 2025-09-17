namespace VpnHood.AppLib.WebServer.Mvc;

public class HttpPutAttribute : RouteAttribute
{
    public HttpPutAttribute(string template = "") : base(template) { }
}