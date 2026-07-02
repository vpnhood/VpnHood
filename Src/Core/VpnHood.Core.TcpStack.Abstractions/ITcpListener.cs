namespace VpnHood.Core.TcpStack.Abstractions;

/// <summary>
/// Represents a TCP listener that accepts incoming connections from an <see cref="ITcpStack"/>.
/// Analogous to <see cref="System.Net.Sockets.TcpListener"/> but for custom TCP stacks.
/// </summary>
public interface ITcpListener : IDisposable
{
    /// <summary>
    /// Asynchronously yields each accepted <see cref="ITcpClient"/> as it arrives.
    /// Completes when the listener is stopped/disposed; throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<ITcpClient> AcceptAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously accepts a single incoming connection.
    /// </summary>
    ValueTask<ITcpClient> AcceptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the listener and disposes any unaccepted connections.
    /// </summary>
    void Stop();
}
