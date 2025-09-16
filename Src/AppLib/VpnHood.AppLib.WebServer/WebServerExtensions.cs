using System.Text.Json;
using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer;

internal static class WebServerExtensions
{
    public static T GetRequestData<T>(this HttpContextBase httpContext)
    {
        var json = httpContext.Request.DataAsString;
        var res = JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });

        return res ?? throw new InvalidOperationException($"The request expected to have a {typeof(T).Name} but it is null!");
    }

    public static async Task SendJsonAsync(this HttpContextBase ctx, object? data, int statusCode = 200)
    {
        if (data is null)
        {
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send();
            return;
        }

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.Send(json);
    }
}