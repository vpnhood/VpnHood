using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Options for creating a QUIC listener. Only the parts that vary per VpnHood endpoint are exposed;
/// TLS 1.3, the ALPN token and encryption policy are fixed by the implementation.
/// </summary>
public sealed class QuicListenerOptions
{
    public required IPEndPoint ListenEndPoint { get; init; }
    public required TimeSpan IdleTimeout { get; init; }

    // Preserve the transport's defaults. An application protocol can choose its own limits and codes.
    public int MaxInboundBidirectionalStreams { get; init; } = 100;
    public int MaxInboundUnidirectionalStreams { get; init; } = 10;
    public long DefaultStreamErrorCode { get; init; }
    public long DefaultCloseErrorCode { get; init; }

    /// <summary>Selects the server certificate for the given SNI host name (null = no SNI provided).</summary>
    public required Func<string?, X509Certificate> ServerCertificateSelector { get; init; }
}
