using System.Net;
using System.Net.Quic;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Quic.MsQuic;

internal sealed class MsQuicServerConnection(QuicConnection connection) : IQuicServerConnection
{
    public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

    public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).Vhc();
        return new MsQuicServerStream(stream);
    }

    public async ValueTask<Stream> OpenOutboundUnidirectionalStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken).Vhc();
        return new MsQuicServerStream(stream);
    }

    public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await connection.AcceptInboundStreamAsync(cancellationToken).Vhc();
        return new MsQuicServerStream(stream);
    }

    public ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken) =>
        connection.CloseAsync(errorCode, cancellationToken);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
