using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicServer(IQuicServer server) : IQuicServer
{
    private BufferedQuicRequest? _nextRequest;

    public IDisposable BufferNextRequest(int byteCount, Action onPrefixConsumed, bool finish,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new BufferedQuicRequest(this, byteCount, onPrefixConsumed, finish,
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        if (Interlocked.CompareExchange(ref _nextRequest, request, null) != null) {
            request.Dispose();
            throw new InvalidOperationException("A request is already waiting to be buffered.");
        }
        return request;
    }

    internal BufferedQuicRequest? TakeRequest()
    {
        var request = Volatile.Read(ref _nextRequest);
        return request is { } && request.TryClaim() ? request : null;
    }

    internal void ReleaseRequest(BufferedQuicRequest request) =>
        Interlocked.CompareExchange(ref _nextRequest, null, request);

    public async ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options,
        CancellationToken cancellationToken)
    {
        var listener = await server.ListenAsync(options, cancellationToken);
        return new BufferedQuicListener(listener, this);
    }
}
