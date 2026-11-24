using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// the system's sockets, with a call before each TCP client it makes, given the endpoint the client is for
internal sealed class DialHookSocketFactory(Action<IPEndPoint> onCreate) : TestSocketFactory
{
    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        onCreate(ipEndPoint);
        return base.CreateTcpClient(ipEndPoint, options);
    }
}
