using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ProxyConnectionTest : TestBase
{
    [TestMethod]
    public void A_proxy_connection_whose_stream_fails_to_close_still_closes_its_socket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var tcpClient = new TcpClient();
        tcpClient.Connect((IPEndPoint)listener.LocalEndpoint);
        var connection = new ProxyConnection(tcpClient, new CloseRecordingStream { FailSyncClose = true });

        connection.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => tcpClient.GetStream());
    }
}
