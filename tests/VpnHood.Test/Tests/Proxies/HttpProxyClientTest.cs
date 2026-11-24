using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Proxies.HttpProxyClients;
using VpnHood.Net.Proxies.HttpProxyServers;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class HttpProxyClientTest : TestBase
{
    [TestMethod]
    public async Task The_stream_of_a_connection_through_an_HTTPS_proxy_carries_its_data()
    {
        // the socket's own stream went around the proxy's TLS session
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var httpsProxyServer = new HttpsProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate
        });

        httpsProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var echoTask = EchoFirstBytes(target, 5);

        // a client that takes the test's certificate, as the connectors take a trusted one
        var proxyClient = new HttpProxyClient(new HttpProxyClientOptions {
            ProxyEndPoint = httpsProxyServer.ListenerEndPoint,
            UseTls = true,
            AllowInvalidCertificates = true
        });

        using var tcpClient = new TestSocketFactory().CreateTcpClient(proxyClient.ProxyEndPoint);
        await proxyClient.ConnectAsync(tcpClient, (IPEndPoint)target.LocalEndpoint, TestCt);
        using var connection = ProxyClientFactory.GetConnection(proxyClient, tcpClient);

        await connection.Stream.WriteAsync("hello"u8.ToArray(), TestCt);
        await connection.Stream.FlushAsync(TestCt);
        var buffer = new byte[5];
        await connection.Stream.ReadExactlyAsync(buffer, TestCt).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.AreEqual("hello", System.Text.Encoding.ASCII.GetString(buffer));
        await echoTask;
    }

    // the first bytes of the first connection the listener takes, sent back
    private async Task EchoFirstBytes(TcpListener listener, int count)
    {
        using var client = await listener.AcceptTcpClientAsync(TestCt);
        var stream = client.GetStream();
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, TestCt);
        await stream.WriteAsync(buffer, TestCt);
    }
}
