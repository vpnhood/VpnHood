using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Proxies;

// Values match the engine's, member for member; the mapper is exhaustive so a protocol added there
// breaks this build instead of reaching a UI as an unknown.
[JsonConverter(typeof(JsonStringEnumConverter<ProxyProtocol>))]
public enum ProxyProtocol
{
    Socks4 = 1,
    Socks5 = 2,
    Http = 3,
    Https = 4
}
