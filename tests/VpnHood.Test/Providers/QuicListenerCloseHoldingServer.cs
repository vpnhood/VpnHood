using System.Net;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

// A QUIC server whose listeners take no connection and whose closes wait until released, as a server host's close
// that waits for its QUIC peers. Its listens can be held too, as a configuration under way
public class QuicListenerCloseHoldingServer : IQuicServer
{
    private readonly TaskCompletionSource _closesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int HeldCloseCount;
    public TestHold ListenHold { get; } = new();

    public void ReleaseCloses() => _closesReleased.TrySetResult();

    public async ValueTask<IQuicListener> ListenAsync(QuicListenerOptions options,
        CancellationToken cancellationToken)
    {
        await ListenHold.WaitIfHeld();
        return new HoldingQuicListener(options.ListenEndPoint, this);
    }

    private sealed class HoldingQuicListener(IPEndPoint localEndPoint, QuicListenerCloseHoldingServer owner)
        : IQuicListener
    {
        public IPEndPoint LocalEndPoint => localEndPoint;

        // no client connects: it waits for the host's cancellation
        public ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken) =>
            new(new TaskCompletionSource<IQuicConnection>().Task.WaitAsync(cancellationToken));

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref owner.HeldCloseCount);
            await owner._closesReleased.Task;
        }
    }
}
