namespace VpnHood.Net.TcpStack.Abstractions;

/// <summary>
/// Represents a listener that accepts the connections an <see cref="ITcpStack"/> completes, the way
/// <see cref="System.Net.Sockets.TcpListener"/> does for sockets.
/// </summary>
public interface ITcpListener : IDisposable
{
    /// <summary>
    /// Accepts connections as they complete their handshake, until the listener is stopped.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The accepted connections; the caller owns each and disposes it.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    IAsyncEnumerable<ITcpClient> AcceptAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Accepts the next connection that completes its handshake.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the accepted connection, which the caller owns and disposes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The listener is stopped.</exception>
    ValueTask<ITcpClient> AcceptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the listener: ends its accepts and closes the connections in its queue; a handshake under way for it is
    /// closed when it completes. Connections already accepted stay open.
    /// </summary>
    void Stop();
}
