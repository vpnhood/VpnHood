using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProxyServerType
{
    Socks5 = 0,
    Socks4 = 1,
    Http = 2,
    Https = 3
}