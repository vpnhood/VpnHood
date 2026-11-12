using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Proxies;

// Values match the engine's, member for member; the mapper is exhaustive so a protocol added there
// breaks this build instead of reaching a UI as an unknown.

/// <summary>
/// Specifies a proxy's protocol.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know fails to read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ProxyProtocol>))]
public enum ProxyProtocol
{
    /// <summary>SOCKS4, whose default port is 1080.</summary>
    Socks4 = 1,

    /// <summary>SOCKS5, whose default port is 1080.</summary>
    Socks5 = 2,

    /// <summary>An HTTP proxy, by <c>CONNECT</c>, whose default port is 8080.</summary>
    Http = 3,

    /// <summary>An HTTP proxy reached over TLS, whose default port is 443.</summary>
    Https = 4
}
