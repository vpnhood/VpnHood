using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.Management.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<ProxyProtocol>))]
public enum ProxyProtocol
{
    Socks4 = 1,
    Socks5 = 2,
    Http = 3,
    Https = 4
}
