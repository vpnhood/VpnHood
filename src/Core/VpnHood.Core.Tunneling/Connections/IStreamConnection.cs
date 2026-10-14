using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public interface IStreamConnection : IDisposable, IAsyncDisposable
{
    string ConnectionId { get; set; } // for diagnostic purposes
    string ConnectionName { get; } // for diagnostic purposes
    bool IsServer { get; } // for diagnostic purposes
    bool Connected { get; }
    Stream Stream { get; }
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
    bool RequireHttpResponse { get; set; }

    // its disposal then closes the connection rather than keeping it for another request; a decorator passes it on,
    // so it reaches the reuse layer however the connection is wrapped
    void PreventReuse();
}