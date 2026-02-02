using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public interface IConnection : IDisposable, IAsyncDisposable
{
    string ConnectionId { get; set; }
    bool Connected { get; }
    Stream Stream { get; }
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
    bool RequireHttpResponse { get; set; }
}