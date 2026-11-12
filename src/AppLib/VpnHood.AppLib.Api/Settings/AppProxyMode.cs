using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Specifies which proxy the app's VPN connects through.</summary>
/// <remarks>In JSON it is a name; a name this version does not know fails to read.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AppProxyMode>))]
public enum AppProxyMode
{
    /// <summary>No proxy: the VPN connects directly.</summary>
    NoProxy,

    /// <summary>The device's own proxy, as its settings tell it.</summary>
    Device,

    /// <summary>The proxies the person lists in the app.</summary>
    Manual
}
