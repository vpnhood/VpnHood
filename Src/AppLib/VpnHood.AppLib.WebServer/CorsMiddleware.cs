using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer;

internal static class CorsMiddleware
{
    private static readonly string[] AllowedOrigins = [
        "https://localhost:8080",
        "http://localhost:8080",
        "https://localhost:8081",
        "http://localhost:8081",
        "http://localhost:30080"
    ];

    public static void AddCors(HttpContextBase ctx, bool isDebugMode)
    {
        var allowedOrigins = isDebugMode ? ["*"] : AllowedOrigins;
        var cors = string.Join(", ", allowedOrigins);

        // Optional: cache preflight response
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", cors);
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
        ctx.Response.Headers["Access-Control-Max-Age"] = ((int)TimeSpan.FromHours(1).TotalSeconds).ToString();
    }
}