using System.Net;

namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Represents an established QUIC connection, over which either side opens and accepts streams. VpnHood uses QUIC as a
/// transport, so its streams are plain <see cref="Stream"/> objects.
/// </summary>
/// <remarks>
/// The caller owns the connection and each stream it returns, and disposes them. Disposing the connection closes it,
/// and disposes the streams the peer opened that nobody accepted.
/// </remarks>
public interface IQuicConnection : IAsyncDisposable
{
    /// <summary>Gets the connection's local address and port.</summary>
    /// <remarks>The Android and iOS clients do not know it, and give the any address with port 0.</remarks>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets the connection's remote address and port.</summary>
    IPEndPoint RemoteEndPoint { get; }

    /// <summary>Opens a bidirectional stream.</summary>
    /// <remarks>
    /// MsQuic waits for the peer's stream allowance; the iOS client may fail at once instead.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the stream, which the caller owns and disposes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="IOException">The stream could not be opened, as when the connection is closed.</exception>
    ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Accepts the next stream the peer opens: a bidirectional one, or on a server, also a unidirectional one, which
    /// only reads.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the stream, which the caller owns and disposes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The connection is closed, on the Android and iOS clients.</exception>
    /// <exception cref="IOException">
    /// The connection is closed, on MsQuic, or the stream could not start, on iOS.
    /// </exception>
    ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken);
}
