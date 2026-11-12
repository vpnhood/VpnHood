using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Specifies which proxies the VPN connects through.
/// </summary>
/// <remarks>In JSON it is a name; a name this version does not know reads as <see cref="None"/>.</remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ProxyMode>))]
[JsonEnumFallback(ProxyMode.None)]
public enum ProxyMode
{
    /// <summary>No proxy; connect directly.</summary>
    None = 0,

    /// <summary>A single proxy endpoint carried inline in the options (device proxy or a
    /// library-supplied proxy). Lightweight path: no persistence, no auto-update, no rating.</summary>
    Simple = 1,

    /// <summary>The managed proxy list stored in the shared endpoint database
    /// (rotation, rating, auto-update).</summary>
    Managed = 2
}
