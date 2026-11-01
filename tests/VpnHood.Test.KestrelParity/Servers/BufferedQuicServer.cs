using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicServer(IQuicServer server, Http3RequestGate gate, int byteCount,
    int headerByteCount, bool finish) : IQuicServer
{
    private int _claimed;

    public int ByteCount => byteCount;
    public int HeaderByteCount => headerByteCount;
    public bool Finish => finish;
    public Http3RequestGate Gate => gate;

    public bool TryClaimRequest() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

    public async ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options,
        CancellationToken cancellationToken)
    {
        var listener = await server.ListenAsync(options, cancellationToken);
        return new BufferedQuicListener(listener, this);
    }
}
