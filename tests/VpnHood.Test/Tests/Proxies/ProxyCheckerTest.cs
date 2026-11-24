using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Server.Access;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ProxyCheckerTest : ProxyTestBase
{
    private static async Task FakeListener(TcpListener tcpListener)
    {
        // Accept connections in background and send invalid proxy responses
        while (true) {
            try {
                var client = await tcpListener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                // Send invalid response to fail proxy handshake
                await stream.WriteAsync("Invalid HTTP\r\n\r\n"u8.ToArray());
                var buf = new byte[0xffff];
                _ = await stream.ReadAsync(buf);
                client.Close();
            }
            catch {
                break;
            }
        }
    }

    [TestMethod]
    public async Task RemoveBadServers_Marks_Unsupported_Types_As_Inactive()
    {
        // Create two TCP listeners that simulate non-SOCKS4 proxy servers by returning invalid responses: an HTTP
        // endpoint is never disabled so, as its proxy's refusal of the server comes as the same error
        using var listener1 = new TcpListener(IPAddress.Loopback, 0);
        using var listener2 = new TcpListener(IPAddress.Loopback, 0);
        listener1.Start();
        listener2.Start();
        _ = FakeListener(listener1);
        _ = FakeListener(listener2);

        var endPoint1 = (IPEndPoint)listener1.LocalEndpoint;
        var endPoint2 = (IPEndPoint)listener2.LocalEndpoint;

        var socketFactory = new TestSocketFactory();
        var store = await CreateStore(
            new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks4,
                Host = endPoint1.Address.ToString(),
                Port = endPoint1.Port
            },
            new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks4,
                Host = endPoint2.Address.ToString(),
                Port = endPoint2.Port
            });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        await mgr.CheckServers(socketFactory, new IPEndPoint(IPAddress.Loopback, 443), hostName: null,
            serverCertificateValidation: null, TestCt);

        // All servers that are no SOCKS4 proxy should be marked as inactive
        Assert.IsTrue(mgr.GetEndPointInfos().All(status => !status.EndPoint.IsEnabled));
    }

    [TestMethod]
    public async Task A_check_goes_through_the_proxy_to_the_VPN_server_alone_and_names_its_host()
    {
        // it went to Cloudflare's 1.1.1.1:443 and named one.one.one.one, a third party in the device's path
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var vpnServer = new TcpListener(IPAddress.Loopback, 0);
        vpnServer.Start();
        var hostNameTask = AcceptTls(vpnServer, certificate);

        using var proxy = new TestSocks5Proxy().Start();
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = true },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        var vpnServerEndPoint = (IPEndPoint)vpnServer.LocalEndpoint;
        await connector.CheckServers(new TestSocketFactory(), vpnServerEndPoint, "vpn.example",
            serverCertificateValidation: null, TestCt);

        CollectionAssert.AreEqual(new[] { vpnServerEndPoint }, proxy.Destinations.ToArray());
        Assert.AreEqual("vpn.example", await hostNameTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(connector.GetEndPointInfos().Single().Status.IsLastUsedSucceeded);
    }

    // a TLS server that takes h2 alone, which no check offers, so each handshake fails
    private async Task ServeH2Only(TcpListener listener, X509Certificate2 certificate)
    {
        while (true) {
            using var client = await listener.AcceptTcpClientAsync(TestCt);
            await using var sslStream = new SslStream(client.GetStream());
            try {
                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = certificate,
                    ApplicationProtocols = [SslApplicationProtocol.Http2]
                }, TestCt);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException) {
                // the failed handshake the server is for
            }
        }
    }

    [TestMethod]
    public async Task A_proxy_whose_TLS_check_with_the_server_fails_stays_enabled()
    {
        // the server's fault as much as the proxy's: such a proxy was disabled
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var vpnServer = new TcpListener(IPAddress.Loopback, 0);
        vpnServer.Start();
        _ = ServeH2Only(vpnServer, certificate);

        using var proxy = new TestSocks5Proxy(standIn: (IPEndPoint)vpnServer.LocalEndpoint).Start();
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = true },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)vpnServer.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        var info = connector.GetEndPointInfos().Single();
        Assert.IsTrue(info.Status.IsLastUsedFailed);
        Assert.IsTrue(info.EndPoint.IsEnabled);
    }

    // A check's time with no end, which a caller may give the connector, ran out at once where the check's time ran
    // from its dial: every check failed. It has no end
    [TestMethod]
    public async Task A_check_whose_time_has_no_end_passes()
    {
        using var vpnServer = new PausingTlsServer().Start();
        using var proxy = new TestSocks5Proxy().Start();
        var store = await CreateStore(ToProxyEndPoint(proxy.EndPoint));
        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: Timeout.InfiniteTimeSpan, TestCt);

        await connector.CheckServers(new TestSocketFactory(), vpnServer.EndPoint, hostName: null,
            serverCertificateValidation: null, TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        Assert.IsTrue(connector.GetEndPointInfos().Single().Status.IsLastUsedSucceeded, "The check failed.");
    }

    // A check's time ran from before its socket was made, which the VPN's socket factory may take long over, as it
    // keeps the socket out of the VPN: such a check could run out its time before it dialed. It runs from the socket's
    // making, as it did before the checks dialed first: its first socket's, and the one it makes after a wait
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_checks_time_runs_from_its_sockets_making(bool isAfterWait)
    {
        using var vpnServer = new PausingTlsServer().Start();
        using var proxy = new TestSocks5Proxy().Start();
        var store = await CreateStore(ToProxyEndPoint(proxy.EndPoint));
        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(4), TestCt);

        // the check before a connect makes one socket, the slow one; a connection's own check that waits for its place,
        // as here where none is free until the test gives one, makes its second, the slow one, after its wait
        var socketFactory = new SlowSocketFactory(TimeSpan.FromSeconds(4.5), slowSocketNumber: isAfterWait ? 2 : 1);
        if (isAfterWait) {
            connector.SearchHandshakePlaces.Dispose();
            connector.SearchHandshakePlaces = new SemaphoreSlim(0);
            var connecting = connector.ConnectAsync(socketFactory, vpnServer.EndPoint, hostName: null,
                serverCertificateValidation: null, onAttempt: null, TestCt);

            await AssertEqualsWait(1, () => proxy.BareCloseCount, "The check did not wait for its place.");
            connector.SearchHandshakePlaces.Release();
            using var connection = await connecting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        }
        else {
            await connector.CheckServers(socketFactory, vpnServer.EndPoint, hostName: null,
                serverCertificateValidation: null, TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        }

        var status = connector.GetEndPointInfos().Single().Status;
        Assert.AreEqual(0, status.FailedCount, "The check's time ran out in the making of its socket.");
        Assert.IsTrue(status.IsLastUsedSucceeded, "The check did not pass.");

        // the server's one hello is the check's: a connection asks no TLS of it here
        Assert.AreEqual(1, vpnServer.HelloTimes.Count, "The check made no handshake.");
    }

    [TestMethod]
    public async Task A_canceled_check_keeps_the_failures_that_came_before_its_cancellation()
    {
        // a canceled run recorded no failure at all. A connect canceled before a pass takes its check with it, which
        // otherwise goes on behind the connect
        using var closedPort = new TcpListener(IPAddress.Loopback, 0);
        closedPort.Start();
        var refusingEndPoint = (IPEndPoint)closedPort.LocalEndpoint;
        closedPort.Stop();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var silentEndPoint = (IPEndPoint)silentProxy.LocalEndpoint;

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(refusingEndPoint), ToProxyEndPoint(silentEndPoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var checkTask = connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, checkCts.Token);

        await AssertEqualsWait(1, () => connector.Progress?.Completed ?? 0, "The refusing proxy's check should end.",
            timeout: 10000);

        await checkCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => checkTask);
        await AssertEqualsWait(true, () => connector.Progress is null, "The canceled connect's check went on.");

        var infos = connector.GetEndPointInfos();
        Assert.AreEqual(1, infos.Single(x => x.EndPoint.Port == refusingEndPoint.Port).Status.FailedCount);
        Assert.AreEqual(0, infos.Single(x => x.EndPoint.Port == silentEndPoint.Port).Status.FailedCount);
    }

    [TestMethod]
    public async Task An_HTTP_proxy_that_refuses_the_server_stays_enabled()
    {
        // its 403, for a port it does not open, came as the error of no HTTP proxy, which disabled it for every server
        using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        _ = AnswerEveryConnection(proxyListener, "HTTP/1.1 403 Forbidden\r\n\r\n");

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(new ProxyEndPoint {
                Protocol = ProxyProtocol.Http,
                Host = IPAddress.Loopback.ToString(),
                Port = ((IPEndPoint)proxyListener.LocalEndpoint).Port
            }), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, TestCt);

        var info = connector.GetEndPointInfos().Single();
        Assert.IsTrue(info.Status.IsLastUsedFailed);
        Assert.IsTrue(info.EndPoint.IsEnabled);
    }

    // a server that answers each connection with the same text, after its first read
    private async Task AnswerEveryConnection(TcpListener listener, string answer)
    {
        while (true) {
            using var client = await listener.AcceptTcpClientAsync(TestCt);
            var stream = client.GetStream();
            try {
                _ = await stream.ReadAsync(new byte[4096], TestCt);
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(answer), TestCt);
            }
            catch (IOException) {
                // the client's close
            }
        }
    }

    [TestMethod]
    public async Task A_check_fails_a_proxy_whose_server_shows_a_certificate_the_server_check_refuses()
    {
        // any certificate passed, so a proxy that stands in for the server, with one of its own, rated well
        using var serverCertificate = CertificateUtil.CreateSelfSigned();
        using var otherCertificate = CertificateUtil.CreateSelfSigned();
        using var vpnServer = new TcpListener(IPAddress.Loopback, 0);
        vpnServer.Start();
        _ = AcceptTls(vpnServer, otherCertificate);

        using var proxy = new TestSocks5Proxy().Start();
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = true },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        var serverHash = serverCertificate.GetCertHash();
        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)vpnServer.LocalEndpoint, "vpn.example",
            (_, certificate, _, _) => certificate?.GetCertHash().SequenceEqual(serverHash) == true, TestCt);

        var info = connector.GetEndPointInfos().Single();
        Assert.IsTrue(info.Status.IsLastUsedFailed);
        Assert.IsTrue(info.EndPoint.IsEnabled);
    }
}
