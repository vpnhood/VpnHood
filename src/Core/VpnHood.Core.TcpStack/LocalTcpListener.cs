using System.Threading.Channels;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// TCP listener that accepts incoming connections on a local endpoint.
/// Similar to <see cref="System.Net.Sockets.TcpListener"/> but for the local TCP stack.
/// </summary>
public sealed class LocalTcpListener : ITcpListener
{
    private readonly Channel<LocalTcpClient> _acceptQueue;

    private readonly LocalTcpStack _stack;
    private bool _stopped;

    /// <summary>
    /// The local endpoint this listener is bound to. Null = wildcard listener (any IPv4/IPv6).
    /// </summary>
    public IpEndPointValue? LocalEndPoint { get; }

    /// <summary>
    /// True when this listener accepts connections on any endpoint.
    /// </summary>
    public bool IsAny => LocalEndPoint is null;

    /// <param name="localEndPoint">The local endpoint this listener is bound to. Null = wildcard listener (any IPv4/IPv6).</param>
    /// <param name="acceptQueueCapacity">
    /// Maximum number of accepted-but-not-yet-accepted clients to buffer. 0 or less = unbounded
    /// (historical behavior). When bounded and full, <see cref="TryEnqueueAccept"/> returns false
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

    /// <summary>
    /// Asynchronously accepts all incoming connections.
    /// </summary>
    public IAsyncEnumerable<LocalTcpClient> AcceptAllAsync(CancellationToken cancellationToken = default)
    {
        return _acceptQueue.Reader.ReadAllAsync(cancellationToken);
    }

    async IAsyncEnumerable<ITcpClient> ITcpListener.AcceptAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var client in AcceptAllAsync(cancellationToken).Vhc())
            yield return client;
    }

    /// <summary>
    /// Asynchronously accepts a single incoming connection.
    /// </summary>
    public ValueTask<LocalTcpClient> AcceptAsync(CancellationToken cancellationToken = default)
    {
        return _acceptQueue.Reader.ReadAsync(cancellationToken);
    }

    async ValueTask<ITcpClient> ITcpListener.AcceptAsync(CancellationToken cancellationToken)
    {
        return await AcceptAsync(cancellationToken).Vhc();
    }

    /// <summary>
    /// Stops the listener and completes the accept queue. Disposes any unaccepted streams.
    /// </summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, true)) return;

        _acceptQueue.Writer.TryComplete();

        if (LocalEndPoint.HasValue)
            _stack.StopListening(LocalEndPoint.Value);
        else
            _stack.StopListeningAny();

        // Dispose any unaccepted clients to release their connections.
        while (_acceptQueue.Reader.TryRead(out var client))
            client.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
