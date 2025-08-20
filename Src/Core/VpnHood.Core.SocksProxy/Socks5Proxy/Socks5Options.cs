using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public class Socks5Options
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}