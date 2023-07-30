using System;
using System.Threading.Tasks;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling.ClientStreams;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorRequestResult<T> : IAsyncDisposable where T : SessionResponseBase
{
    public required IClientStream ClientStream { get; init; }
    public required T Response { get; init; }

    public async ValueTask DisposeAsync()
    {
        await ClientStream.DisposeAsync();
    }
}