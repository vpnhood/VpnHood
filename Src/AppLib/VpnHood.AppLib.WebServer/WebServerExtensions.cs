using System.Text.Json;
using EmbedIO;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.WebServer;

internal static class WebServerExtensions
{
    public static async Task<T> GetRequestDataAsync<T>(this IHttpContext httpContext)
    {
        var json = await httpContext.GetRequestBodyAsByteArrayAsync().VhConfigureAwait();
        var res = JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return res ?? throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
    }
}