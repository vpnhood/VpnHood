using System.Net;
using System.Net.Quic;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Quic.MsQuic;

internal sealed class MsQuicListener(QuicListener listener) : IQuicListener
{
    public IPEndPoint LocalEndPoint => listener.LocalEndPoint;

    public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await listener.AcceptConnectionAsync(cancellationToken).Vhc();
        return new MsQuicConnection(connection);
    }

    public ValueTask DisposeAsync()
    {
        return listener.DisposeAsync();
    }
}
