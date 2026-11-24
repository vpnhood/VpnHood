using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

internal sealed class EndPointRecordingSocketFactory : TestSocketFactory
{
    public ConcurrentQueue<IPEndPoint> EndPoints { get; } = new();

    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        EndPoints.Enqueue(ipEndPoint);
        return base.CreateTcpClient(ipEndPoint, options);
    }
}
