using VpnHood.Common.Messaging;
using VpnHood.Tunneling.ClientStreams;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorRequestResult<T> : IAsyncDisposable where T : SessionResponse
{
    public required IClientStream ClientStream { get; init; }
    public required T Response { get; init; }

    public ValueTask DisposeAsync()
    {
        return ClientStream.DisposeAsync();
    }
}