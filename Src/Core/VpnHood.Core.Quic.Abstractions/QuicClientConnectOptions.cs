using System.Net;
using System.Net.Security;

namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// Options for establishing an outbound QUIC connection. Only the parts that vary per VpnHood
/// connection are exposed; TLS 1.3, the ALPN token, encryption policy and error codes are fixed
/// by the implementation.
/// </summary>
public sealed class QuicClientConnectOptions
{
    public required IPEndPoint RemoteEndPoint { get; init; }
    public required string TargetHost { get; init; }
    public required RemoteCertificateValidationCallback CertificateValidationCallback { get; init; }
}
