using System.Net;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

internal sealed class QuicRecoveryConnection(IQuicConnection connection, QuicRecoverySocketFactory owner)
    : IQuicConnection
{
    public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

    public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.RejectQuicStreamOpens)
            throw new IOException("The test rejected the QUIC stream open.");
        var stream = await connection.OpenOutboundStreamAsync(cancellationToken);
        owner.RecordQuicStreamOpen();
        return stream;
    }

    public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken) =>
        connection.AcceptInboundStreamAsync(cancellationToken);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
