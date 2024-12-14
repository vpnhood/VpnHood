using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorRequestResult<T> : IAsyncDisposable where T : SessionResponse
{
    public required IClientStream ClientStream { get; init; }
    public required T Response { get; init; }

    public ValueTask DisposeAsync()
    {
        return ClientStream.DisposeAsync();
    }
}