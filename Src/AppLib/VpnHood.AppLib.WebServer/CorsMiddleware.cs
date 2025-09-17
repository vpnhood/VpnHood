using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer;

internal static class CorsMiddleware
{
    public static void AddCors(HttpContextBase ctx)
    {
        var cors = VpnHoodApp.Instance.Features.IsDebugMode 
            ? "*" 
            : "https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081, http://localhost:30080";

        ctx.Response.Headers.Add("Access-Control-Allow-Origin", cors);
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
    }
}