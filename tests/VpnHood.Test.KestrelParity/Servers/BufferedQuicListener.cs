using System.Net;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class BufferedQuicListener(IQuicListener listener, BufferedQuicServer server) : IQuicListener
{
    public IPEndPoint LocalEndPoint => listener.LocalEndPoint;

    public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await listener.AcceptConnectionAsync(cancellationToken);
        if (connection is IQuicServerConnection serverConnection)
            return new BufferedQuicConnection(serverConnection, server);
        await connection.DisposeAsync();
        throw new InvalidOperationException("The test listener requires a server QUIC connection.");
    }

    public ValueTask DisposeAsync() => listener.DisposeAsync();
}
