using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorRequestResult<T> : IDisposable where T : SessionResponse
{
    public required IStreamConnection StreamConnection { get; init; }
    public required T Response { get; init; }

    public void Dispose()
    {
        StreamConnection.Dispose();
    }
}