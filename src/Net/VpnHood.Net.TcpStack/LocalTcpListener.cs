using System.Threading.Channels;
using VpnHood.Net.TcpStack.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack;

/// <summary>
/// Provides a listener of a <see cref="LocalTcpStack"/>: it queues the connections that complete their handshake until
/// they are accepted, the way <see cref="System.Net.Sockets.TcpListener"/> does for sockets.
/// </summary>
/// <remarks>
/// A full queue (<see cref="LocalTcpStackOptions.AcceptQueueCapacity"/>) closes a new connection instead.
/// </remarks>
public sealed class LocalTcpListener : ITcpListener
{
    private readonly Channel<LocalTcpClient> _acceptQueue;

    private readonly LocalTcpStack _stack;
    private bool _stopped;

    /// <summary>
    /// Gets the endpoint this listener takes connections to, or <see langword="null"/> for the listener of
    /// <see cref="LocalTcpStack.ListenAny"/>, which takes the connections no endpoint listener takes.
    /// </summary>
    public IpEndPointValue? LocalEndPoint { get; }

    /// <summary>
    /// Gets a value indicating whether this is the listener of <see cref="LocalTcpStack.ListenAny"/>.
    /// </summary>
    public bool IsAny => LocalEndPoint is null;

    /// <param name="localEndPoint">The local endpoint this listener is bound to. Null = wildcard listener (any IPv4/IPv6).</param>
    /// <param name="acceptQueueCapacity">
    /// Maximum number of established-but-not-yet-accepted clients to buffer. 0 or less = unbounded.
    /// When bounded and full, <see cref="TryEnqueueAccept"/> returns false
    /// (FullMode = Wait makes TryWrite fail rather than block), so the caller disposes the client.
    /// </param>
    /// <param name="stack">The local TCP stack this listener is associated with.</param>
    internal LocalTcpListener(LocalTcpStack stack, IpEndPointValue? localEndPoint, int acceptQueueCapacity)
    {
        _stack = stack;
        LocalEndPoint = localEndPoint;
        // SingleReader is intentionally NOT set: Stop() drains the queue with TryRead concurrently with
        // the consumer's accept loop, which would violate the single-reader contract's lock-free path.
        _acceptQueue = acceptQueueCapacity > 0
            ? Channel.CreateBounded<LocalTcpClient>(new BoundedChannelOptions(acceptQueueCapacity) {
                FullMode = BoundedChannelFullMode.Wait // TryWrite returns false when full -> caller disposes
            })
            : Channel.CreateUnbounded<LocalTcpClient>();
    }

    /// <summary>
    /// Try to enqueue an accepted stream. Returns false if the listener has been stopped,
    /// in which case the caller is responsible for disposing the stream.
    /// </summary>
    internal bool TryEnqueueAccept(LocalTcpClient client)
    {
        if (Volatile.Read(ref _stopped)) return false;
        return _acceptQueue.Writer.TryWrite(client);
    }

    /// <inheritdoc cref="ITcpListener.AcceptAllAsync"/>
    public IAsyncEnumerable<LocalTcpClient> AcceptAllAsync(CancellationToken cancellationToken)
    {
        return _acceptQueue.Reader.ReadAllAsync(cancellationToken);
    }

    async IAsyncEnumerable<ITcpClient> ITcpListener.AcceptAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var client in AcceptAllAsync(cancellationToken).Vhc())
            yield return client;
    }

    /// <summary>
    /// Accepts the next connection that completes its handshake.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the accepted connection, which the caller owns and disposes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ChannelClosedException">The listener is stopped.</exception>
    public ValueTask<LocalTcpClient> AcceptAsync(CancellationToken cancellationToken)
    {
        return _acceptQueue.Reader.ReadAsync(cancellationToken);
    }

    async ValueTask<ITcpClient> ITcpListener.AcceptAsync(CancellationToken cancellationToken)
    {
        return await AcceptAsync(cancellationToken).Vhc();
    }

    /// <summary>
    /// Stops the listener: ends its accepts, closes the connections in its queue, and leaves the stack, so a new listener
    /// can take its endpoint. A handshake under way for it is closed when it completes; connections already accepted stay
    /// open. Calling it again does nothing.
    /// </summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, true)) return;

        _acceptQueue.Writer.TryComplete();

        _stack.RemoveListener(this);

        // Dispose any unaccepted clients to release their connections.
        while (_acceptQueue.Reader.TryRead(out var client))
            client.Dispose();
    }

    /// <summary>
    /// Stops the listener, as <see cref="Stop"/> does.
    /// </summary>
    public void Dispose() => Stop();
}
