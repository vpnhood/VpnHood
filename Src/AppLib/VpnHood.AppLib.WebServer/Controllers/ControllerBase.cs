using System.Net;
using System.Text.Json;
using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer.Controllers;

public abstract class ControllerBase
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public abstract void AddRoutes(IRouteMapper mapper);

    protected void AddCors(HttpContextBase ctx)
    {
        var cors = VpnHoodApp.Instance.Features.IsDebugMode ? "*" : "https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081, http://localhost:30080";
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", cors);
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
    }

    protected async Task SendJson(HttpContextBase ctx, object? data, int statusCode = 200)
    {
        AddCors(ctx);
        if (data is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NoContent;
            await ctx.Response.Send();
            return;
        }

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.Send(json);
    }

    protected T ReadJson<T>(HttpContextBase ctx)
    {
        try
        {
            var bytes = ctx.Request.DataAsBytes;
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException($"Request body is empty for {typeof(T).Name}");

            // Determine encoding from Content-Type if available
            var encoding = System.Text.Encoding.UTF8;
            var ct = ctx.Request.ContentType;
            if (!string.IsNullOrWhiteSpace(ct))
                try
                {
                    var parts = ct.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var charsetPart = parts.FirstOrDefault(p => p.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));
                    if (charsetPart != null)
                    {
                        var charset = charsetPart.Substring("charset=".Length);
                        encoding = System.Text.Encoding.GetEncoding(charset);
                    }
                }
                catch
                {
                    // ignore invalid charset and default to UTF-8
                }

            var body = encoding.GetString(bytes);

            var obj = JsonSerializer.Deserialize<T>(body, _jsonOptions);
            if (obj == null)
                throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}. Body: {body}");

            return obj;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON for {typeof(T).Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error reading JSON for {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}