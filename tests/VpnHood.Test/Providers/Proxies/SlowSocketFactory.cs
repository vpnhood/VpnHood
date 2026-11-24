using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// the system's sockets, the one of the number given made only after the time given, as a VPN's factory may take
// long to keep a socket out of the VPN
internal sealed class SlowSocketFactory(TimeSpan delay, int slowSocketNumber) : TestSocketFactory
{
    private int _socketCount;

    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        if (Interlocked.Increment(ref _socketCount) == slowSocketNumber)
            Thread.Sleep(delay);

        return base.CreateTcpClient(ipEndPoint, options);
    }
}
