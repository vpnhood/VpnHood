using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

// async only: a connection's sync close may wait for the peer, as a QUIC stream's does
internal class ConnectorRequestResult<T> : IAsyncDisposable where T : SessionResponse
{
    public required IStreamConnection StreamConnection { get; init; }
    public required T Response { get; init; }

    public ValueTask DisposeAsync()
    {
        return StreamConnection.DisposeAsync();
    }
}
