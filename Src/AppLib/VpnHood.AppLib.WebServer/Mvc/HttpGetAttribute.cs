namespace VpnHood.AppLib.WebServer.Mvc;

public class HttpGetAttribute : RouteAttribute
{
    public HttpGetAttribute(string template = "") : base(template) { }
}