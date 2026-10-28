using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client;

public record VpnEndPoint(
    [property: JsonConverter(typeof(IPEndPointConverter))] IPEndPoint TcpEndPoint,
    string HostName,
    byte[]? CertificateHash,
    string? PathBase);