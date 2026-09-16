using WatsonWebserver.Core;

namespace VpnHood.AppLib.Api.WebHost.Helpers;

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

    // Where a request may claim to come from: one of the dev servers above, or the page this
    // server itself served. A browser sets Origin and a page cannot change it, so this is what
    // separates the app's own SPA from any other tab that knows the address. The web server gates
    // every request on the same answer, which is why it lives here and not inside AddCors.
    public static bool IsAllowedOrigin(string origin, string? hostHeader, bool allowAnyOrigin)
    {
        if (allowAnyOrigin)
            return true;

        if (AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return true;

        // the app's own pages: same host and port as the request arrived on, http since that is all
        // this server speaks
        return !string.IsNullOrEmpty(hostHeader) &&
               string.Equals(origin, $"http://{hostHeader}", StringComparison.OrdinalIgnoreCase);
    }

    // One origin echoed back, never a list and never "*": a browser rejects a list outright and
    // refuses "*" for a credentialed request, which the pairing cookie is. An origin that is
    // not allowed gets no CORS headers at all; that is the browser's cue to block the call. A
    // request without Origin is same-origin or not from a browser, and needs nothing.
    public static void AddCors(HttpContextBase ctx, bool allowAnyOrigin)
    {
        var origin = ctx.Request.Headers.Get("Origin");
        if (string.IsNullOrEmpty(origin))
            return;

        if (!IsAllowedOrigin(origin, ctx.Request.RetrieveHeaderValue("Host"), allowAnyOrigin))
            return;

        ctx.Response.Headers.Add("Access-Control-Allow-Origin", origin);
        ctx.Response.Headers.Add("Vary", "Origin");
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
        ctx.Response.Headers["Access-Control-Max-Age"] = ((int)TimeSpan.FromHours(1).TotalSeconds).ToString();
    }
}
