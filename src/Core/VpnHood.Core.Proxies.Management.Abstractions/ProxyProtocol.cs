using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Proxies.Management.Abstractions;

// A name this side does not know reads as Socks5: a proxy of a protocol this side does not know fails as a
// proxy, never sending around it.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ProxyProtocol>))]
[JsonEnumFallback(ProxyProtocol.Socks5)]
public enum ProxyProtocol
{
    Socks4 = 1,
    Socks5 = 2,
    Http = 3,
    Https = 4
}
