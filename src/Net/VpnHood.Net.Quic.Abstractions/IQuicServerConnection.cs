namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Represents a server's QUIC connection, with what an application protocol such as HTTP/3 needs beyond
/// <see cref="IQuicConnection"/>. The streams it opens and accepts also implement <see cref="IQuicStream"/>.
/// </summary>
/// <remarks>
/// Disposing one of its streams synchronously starts the stream's close without waiting for it; MsQuic's
/// <see cref="IAsyncDisposable.DisposeAsync"/> waits for the close with a five-second deadline, and native cleanup may
/// continue afterward.
/// </remarks>
public interface IQuicServerConnection : IQuicConnection
{
    /// <summary>Opens a write-only stream, waiting for the peer's stream allowance.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the stream, which the caller owns and disposes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="IOException">The stream could not be opened, as when the connection is closed.</exception>
    ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection with an application error code, which the peer receives.</summary>
    /// <param name="errorCode">The application protocol's error code; the transport does not interpret it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken);
}
