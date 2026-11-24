using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// the system's sockets, each TCP client it makes kept for the test to look at
internal sealed class RecordingSocketFactory : TestSocketFactory
{
    public ConcurrentQueue<TcpClient> TcpClients { get; } = new();

    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var tcpClient = base.CreateTcpClient(ipEndPoint, options);
        TcpClients.Enqueue(tcpClient);
        return tcpClient;
    }
}
