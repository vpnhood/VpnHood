using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public interface IConnection : IDisposable, IAsyncDisposable
{
    string ConnectionId { get; set; } // for diagnostic purposes
    string ConnectionName { get; } // for diagnostic purposes
    bool IsServer { get; } // for diagnostic purposes
    bool Connected { get; }
    Stream Stream { get; }
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
    bool RequireHttpResponse { get; set; }
}