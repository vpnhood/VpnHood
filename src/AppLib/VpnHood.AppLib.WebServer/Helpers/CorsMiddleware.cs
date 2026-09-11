using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer.Helpers;

internal static class CorsMiddleware
{
    // The SPA's own dev servers. Any other origin needs a debug build or the developer's
    // /remote-access command, both read once at launch (VpnHoodAppWebServer).
    private static readonly string[] AllowedOrigins = [
        "https://localhost:8080",
        "http://localhost:8080",
        "https://localhost:8081",
        "http://localhost:8081",
        "http://localhost:30080"
    ];

    // One origin echoed back, never a list and never "*": a browser rejects a list outright and
    // refuses "*" for a credentialed request, which the pairing cookie will be. An origin that is
    // not allowed gets no CORS headers at all; that is the browser's cue to block the call. A
    // request without Origin is same-origin or not from a browser, and needs nothing.
    public static void AddCors(HttpContextBase ctx, bool allowAnyOrigin)
    {
        var origin = ctx.Request.Headers.Get("Origin");
        if (string.IsNullOrEmpty(origin))
            return;

        if (!allowAnyOrigin && !AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return;

        ctx.Response.Headers.Add("Access-Control-Allow-Origin", origin);
        ctx.Response.Headers.Add("Vary", "Origin");
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
        ctx.Response.Headers["Access-Control-Max-Age"] = ((int)TimeSpan.FromHours(1).TotalSeconds).ToString();
    }
}
