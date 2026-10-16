using System.Net;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

// A test socket factory whose QUIC connections' closes can be held until released, as a close on a dead path waits
// for its timeout
public class QuicCloseHoldingSocketFactory : TestSocketFactory
{
    private readonly TaskCompletionSource _closesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public volatile bool HoldCloses; // closes begun from now on wait for ReleaseCloses
    public int HeldCloseCount;

    public void ReleaseCloses() => _closesReleased.TrySetResult();

    public override IQuicClient CreateQuicClient() => new HoldingQuicClient(base.CreateQuicClient(), this);

    private sealed class HoldingQuicClient(IQuicClient quicClient, QuicCloseHoldingSocketFactory owner)
        : IQuicClient
    {
        public async ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options,
            CancellationToken cancellationToken)
        {
            return new HoldingQuicConnection(await quicClient.ConnectAsync(options, cancellationToken), owner);
        }
    }

    private sealed class HoldingQuicConnection(IQuicConnection connection, QuicCloseHoldingSocketFactory owner)
        : IQuicConnection
    {
        public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

        public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken) =>
            connection.OpenOutboundStreamAsync(cancellationToken);

        public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken) =>
            connection.AcceptInboundStreamAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (owner.HoldCloses) {
                Interlocked.Increment(ref owner.HeldCloseCount);
                await owner._closesReleased.Task;
            }

            await connection.DisposeAsync();
        }
    }
}
