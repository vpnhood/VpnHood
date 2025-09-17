using WatsonWebserver.Core;

namespace VpnHood.AppLib.WebServer;

internal static class HttpContextBaseExtensions
{
    public static string? GetQueryValueString(this HttpContextBase ctx, string key, string? defaultValue = null)
    {
        return ctx.Request.QuerystringExists(key) ? ctx.Request.RetrieveQueryValue(key) : defaultValue;
    }

    public static int? GetQueryValueInt(this HttpContextBase ctx, string key, int? defaultValue = null)
    {
        var valueString = ctx.GetQueryValueString(key);
        return string.IsNullOrWhiteSpace(valueString) ? defaultValue : int.Parse(valueString);
    }

    public static Guid GetQueryValueGuid(this HttpContextBase ctx, string key)
    {
        return ctx.GetQueryValueGuid(key, null)
               ?? throw new ArgumentException($"Query parameter '{key}' is required.");
    }

    public static Guid? GetQueryValueGuid(this HttpContextBase ctx, string key, Guid? defaultValue)
    {
        var valueString = ctx.GetQueryValueString(key);
        return string.IsNullOrWhiteSpace(valueString) ? defaultValue : Guid.Parse(valueString);
    }

    public static T GetQueryValueEnum<T>(this HttpContextBase ctx, string key, T defaultValue = default) where T : struct
    {
        var valueString = ctx.GetQueryValueString(key);
        return string.IsNullOrWhiteSpace(valueString)
            ? defaultValue
            : Enum.Parse<T>(valueString, true);
    }
}
