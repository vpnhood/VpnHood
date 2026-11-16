namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Represents a QUIC client, which connects to a VpnHood server.
/// </summary>
public interface IQuicClient
{
    /// <summary>
    /// Opens a QUIC connection to a server and completes its TLS handshake.
    /// </summary>
    /// <param name="options">The server's endpoint, the name it is reached by and the certificate check.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. Its result is the connection; dispose it to close it.</returns>
    /// <exception cref="NotSupportedException">The platform does not support this client's QUIC.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="IOException">
    /// The connection failed: the server could not be reached, refused it, or the certificate was rejected.
    /// </exception>
    ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options, CancellationToken cancellationToken);
}
