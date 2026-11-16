using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Options for creating a QUIC listener. Only the parts that vary per VpnHood endpoint are exposed;
/// TLS 1.3, the ALPN token and encryption policy are fixed by the implementation.
/// </summary>
public sealed class QuicListenerOptions
{
    /// <summary>Gets the address and port to listen on. Port 0 lets the system choose one.</summary>
    public required IPEndPoint ListenEndPoint { get; init; }

    /// <summary>Gets how long a connection may stay idle before it is closed.</summary>
    public required TimeSpan IdleTimeout { get; init; }

    // Preserve the transport's defaults. An application protocol can choose its own limits and codes.

    /// <summary>Gets the maximum number of bidirectional streams a client may have open on a connection. The default is 100.</summary>
    public int MaxInboundBidirectionalStreams { get; init; } = 100;

    /// <summary>Gets the maximum number of unidirectional streams a client may have open on a connection. The default is 10.</summary>
    public int MaxInboundUnidirectionalStreams { get; init; } = 10;

    /// <summary>Gets the application error code sent when a stream is aborted without one of its own. The default is 0.</summary>
    public long DefaultStreamErrorCode { get; init; }

    /// <summary>Gets the application error code sent when a connection is closed without one of its own. The default is 0.</summary>
    public long DefaultCloseErrorCode { get; init; }

    /// <summary>Selects the server certificate for the given SNI host name (null = no SNI provided).</summary>
    public required Func<string?, X509Certificate> ServerCertificateSelector { get; init; }
}
