using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// Options for creating a QUIC listener. Only the parts that vary per VpnHood endpoint are exposed;
/// TLS 1.3, the ALPN token, encryption policy and error codes are fixed by the implementation.
/// </summary>
public sealed class QuicListenerOptions
{
    public required IPEndPoint ListenEndPoint { get; init; }
    public required TimeSpan IdleTimeout { get; init; }

    /// <summary>Selects the server certificate for the given SNI host name (null = no SNI provided).</summary>
    public required Func<string?, X509Certificate> ServerCertificateSelector { get; init; }
}
