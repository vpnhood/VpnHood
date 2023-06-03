using System;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling.ClientStreams;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorRequestResult<T> : IDisposable where T : SessionResponseBase
{
    public required TcpClientStream TcpClientStream { get; init; }
    public required T Response { get; init; }

    public void Dispose()
    {
        TcpClientStream.Dispose();
    }
}