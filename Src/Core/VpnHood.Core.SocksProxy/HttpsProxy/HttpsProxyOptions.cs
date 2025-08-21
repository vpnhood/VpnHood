using System.Net;
using System.Net.Security;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.SocksProxy.HttpsProxy;

public class HttpsProxyOptions
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public IDictionary<string, string>? ExtraHeaders { get; init; }

    // If true, connect to the proxy over TLS (HTTPS proxy). Otherwise plain HTTP.
    public bool UseTls { get; init; }
    // Optional DNS name to use for TLS SNI and certificate validation. If null, IP will be used.
    public string? TlsHost { get; init; }
    // If true, skips certificate validation (not recommended).
    public bool AllowInvalidCertificates { get; init; }
}
