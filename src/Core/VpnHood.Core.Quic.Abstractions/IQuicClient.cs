namespace VpnHood.Core.Quic.Abstractions;

/// <summary>
/// Client-side QUIC connector. Establishes outbound QUIC connections to a VpnHood server.
/// </summary>
public interface IQuicClient
{
    ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options, CancellationToken cancellationToken);
}
