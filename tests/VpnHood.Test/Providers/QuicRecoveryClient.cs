using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

internal sealed class QuicRecoveryClient(IQuicClient client, QuicRecoverySocketFactory owner) : IQuicClient
{
    public async ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.RejectQuicConnects)
            throw new IOException("The test rejected the QUIC connection.");
        return new QuicRecoveryConnection(await client.ConnectAsync(options, cancellationToken), owner);
    }
}
