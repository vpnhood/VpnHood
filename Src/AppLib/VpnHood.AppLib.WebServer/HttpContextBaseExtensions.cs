using System.ComponentModel;
using System.Net;
using System.Text.Json;
using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer;

internal static class HttpContextBaseExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Query parameter extensions
    public static string? GetQueryParameterString(this HttpContextBase ctx, string key, string? defaultValue = null)
    {
        return ctx.Request.QuerystringExists(key) ? ctx.Request.RetrieveQueryValue(key) : defaultValue;
    }

    public static int? GetQueryParameterInt(this HttpContextBase ctx, string key, int? defaultValue = null)
    {
        var valueString = ctx.GetQueryParameterString(key);
        return string.IsNullOrWhiteSpace(valueString) ? defaultValue : int.Parse(valueString);
    }

    public static Guid GetQueryParameterGuid(this HttpContextBase ctx, string key)
    {
        return ctx.GetQueryParameterGuid(key, null)
               ?? throw new ArgumentException($"Query parameter '{key}' is required.");
    }

    public static Guid? GetQueryParameterGuid(this HttpContextBase ctx, string key, Guid? defaultValue)
    {
        var valueString = ctx.GetQueryParameterString(key);
        return string.IsNullOrWhiteSpace(valueString) ? defaultValue : Guid.Parse(valueString);
    }

    public static T GetQueryParameterEnum<T>(this HttpContextBase ctx, string key, T defaultValue = default) where T : struct
    {
        var valueString = ctx.GetQueryParameterString(key);
        return string.IsNullOrWhiteSpace(valueString)
            ? defaultValue
            : Enum.Parse<T>(valueString, true);
    }

    public static T? GetRouteParameter<T>(this HttpContextBase ctx, string key, T? defaultValue = default)
    {
        if (!ctx.Request.QuerystringExists(key))
            return defaultValue;

        var value = ctx.Request.RetrieveQueryValue(key);
        try {
            return ConvertString<T>(value);
        }
        catch (Exception ex) {
            throw new ArgumentException($"Cannot convert '{value}' to {typeof(T)} for parameter '{key}'", ex);
        }
    }
    
    public static T? GetQueryParameter<T>(this HttpContextBase ctx, string key, T? defaultValue = default)
    {
        var stringValue = ctx.Request.Url.Parameters.Get(key);
        if (stringValue is null)
            return defaultValue;

        try {
            return ConvertString<T>(stringValue);
        }
        catch (Exception ex) {
            throw new ArgumentException($"Cannot convert '{stringValue}' to {typeof(T)} for parameter '{key}'", ex);
        }
    }

    private static T? ConvertString<T>(string value)
    {
        var underlyingType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (underlyingType == typeof(Guid))
            return (T)(object)Guid.Parse(value);

        if (underlyingType.IsEnum)
            return (T)(object)Enum.Parse(underlyingType, value, true);

        if (underlyingType == typeof(bool))
            return (T)(object)bool.Parse(value);

        if (underlyingType == typeof(DateTime))
            return (T)(object)DateTime.Parse(value);

        if (underlyingType == typeof(DateTimeOffset))
            return (T)(object)DateTimeOffset.Parse(value);

        if (underlyingType == typeof(TimeSpan))
            return (T)(object)TimeSpan.Parse(value);

        // Use TypeConverter for complex conversions
        var converter = TypeDescriptor.GetConverter(underlyingType);
        if (converter.CanConvertFrom(typeof(string)))
            return (T?)(object?)converter.ConvertFromString(value);

        return (T)(object)(Convert.ChangeType(value, underlyingType));
    }


    // JSON handling extensions
    public static async Task SendJson(this HttpContextBase ctx, object? data, int statusCode = 200)
    {
        if (data is null) {
            ctx.Response.StatusCode = (int)HttpStatusCode.NoContent;
            await ctx.Response.Send();
            return;
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.Send(json);
    }

    public static T ReadJson<T>(this HttpContextBase ctx)
    {
        try {
            var bytes = ctx.Request.DataAsBytes;
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException($"Request body is empty for {typeof(T).Name}");

            // Determine encoding from Content-Type if available
            var encoding = System.Text.Encoding.UTF8;
            var ct = ctx.Request.ContentType;
            if (!string.IsNullOrWhiteSpace(ct))
                try {
                    var parts = ct.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var charsetPart = parts.FirstOrDefault(p => p.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));
                    if (charsetPart != null) {
                        var charset = charsetPart["charset=".Length..];
                        encoding = System.Text.Encoding.GetEncoding(charset);
                    }
                }
                catch {
                    // ignore invalid charset and default to UTF-8
                }

            var body = encoding.GetString(bytes);

            var obj = JsonSerializer.Deserialize<T>(body, JsonOptions) ??
                      throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}. Body: {body}");

            return obj;
        }
        catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid JSON for {typeof(T).Name}: {ex.Message}");
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Error reading JSON for {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}
