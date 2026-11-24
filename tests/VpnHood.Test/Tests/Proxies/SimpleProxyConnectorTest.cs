using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class SimpleProxyConnectorTest : ProxyTestBase
{
    [TestMethod]
    public async Task A_simple_connector_has_no_success_before_its_proxy_made_a_connection()
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        await using var connector = new SimpleProxyConnector(ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint));
        Assert.IsFalse(connector.Status.IsAnySucceeded);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.IsTrue(connector.Status.IsAnySucceeded);
    }

    [TestMethod]
    public async Task A_simple_connectors_success_right_after_a_failure_is_its_last_use()
    {
        // both times were the coarse clock's one second, and a tie reads as a failure
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        await using var connector = new SimpleProxyConnector(ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint));
        using var failedClient = new TcpClient();
        connector.RecordFailed(failedClient, new IOException("A failure for the test."));
        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.IsTrue(connector.Status.IsAnySucceeded);
    }

    [TestMethod]
    public async Task A_simple_connector_given_another_proxy_has_no_outcome_of_it()
    {
        // the proxy before's success counted for the new one
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        await using var connector = new SimpleProxyConnector(ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint));
        using (await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
                   serverCertificateValidation: null, onAttempt: null, TestCt)) {
        }

        await connector.UpdateOptions(new ProxyOptions {
            Mode = ProxyMode.Simple,
            ProxyEndPoint = ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1))
        }, TestCt);

        Assert.IsFalse(connector.Status.IsAnySucceeded);
    }
}
