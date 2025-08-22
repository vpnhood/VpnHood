using System.Net;

namespace VpnHood.Core.SocksProxy.HttpProxyClients;

public class HttpProxyOptions
{
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? ProxyHost { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; }
    public bool AllowInvalidCertificates { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}
