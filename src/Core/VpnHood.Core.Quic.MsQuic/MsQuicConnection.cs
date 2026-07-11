using System.Net;
using System.Net.Quic;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.MsQuic;

internal sealed class MsQuicConnection(QuicConnection connection) : IQuicConnection
{
    public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

    public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        return await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).Vhc();
    }

    public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        return await connection.AcceptInboundStreamAsync(cancellationToken).Vhc();
    }

    public ValueTask DisposeAsync()
    {
        return connection.DisposeAsync();
    }
}
