using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

// A test socket factory whose QUIC connects can be held until cancelled, as a connect into a network that went
// dead hangs until its timeout
public class QuicConnectHoldingSocketFactory : TestSocketFactory
{
    public volatile bool HoldConnects; // connects begun from now on hang until cancelled
    public int HeldConnectCount;

    public override IQuicClient CreateQuicClient() => new HoldingQuicClient(base.CreateQuicClient(), this);

    private sealed class HoldingQuicClient(IQuicClient quicClient, QuicConnectHoldingSocketFactory owner)
        : IQuicClient
    {
        public async ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options,
            CancellationToken cancellationToken)
        {
            if (owner.HoldConnects) {
                Interlocked.Increment(ref owner.HeldConnectCount);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await quicClient.ConnectAsync(options, cancellationToken);
        }
    }
}
