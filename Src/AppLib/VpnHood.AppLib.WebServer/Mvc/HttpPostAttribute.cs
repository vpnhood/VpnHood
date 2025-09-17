namespace VpnHood.AppLib.WebServer.Mvc;

public class HttpPostAttribute : RouteAttribute
{
    public HttpPostAttribute(string template = "") : base(template) { }
}