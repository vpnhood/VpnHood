using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Specifies a proxy's protocol.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="Socks5"/>, so such a proxy fails as a
/// proxy rather than letting the VPN connect around it.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ProxyProtocol>))]
[JsonEnumFallback(ProxyProtocol.Socks5)]
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
