using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.Management.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<ProxyMode>))]
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
