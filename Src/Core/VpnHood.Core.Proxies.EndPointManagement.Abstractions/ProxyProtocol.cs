using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProxyProtocol
{
    Socks4 = 1,
    Socks5 = 2,
    Http = 3,
    Https = 4
}