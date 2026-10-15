using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

// Dispose and DisposeAsync release the connection: a reusable one ends its logical stream with the peer and goes back
// to its owner for another request, unless PreventReuse came first; DisposeAsync awaits that, Dispose leaves it to
// run. Any other closes
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

    // its release then closes it rather than keeping it for another request; a decorator passes it on, so it
    // reaches the reusable connection however the connection is wrapped
    void PreventReuse();
}