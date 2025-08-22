using System.Net;

namespace VpnHood.Core.SocksProxy.HttpsProxy;

public class HttpsProxyOptions
{
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? ProxyHost { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; }
    public bool AllowInvalidCertificates { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}
