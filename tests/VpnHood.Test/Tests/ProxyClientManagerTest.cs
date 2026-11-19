using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.AppLogic.Api.WebHost.Helpers;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Proxies.HttpProxyClients;
using VpnHood.Net.Proxies;
using VpnHood.Net.Proxies.HttpProxyServers;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Device;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using ClientState = VpnHood.Core.Client.Abstractions.ClientState;

namespace VpnHood.Test.Tests;

[TestClass]
public class ManagedProxyConnectorTest : TestBase
{
    // the store's upsert with its usual flags, under the test's token
    private Task Upsert(IProxyEndPointStore store, IReadOnlyList<ProxyEndPointRecord> records,
        bool keepExistingStatus = true, bool keepExistingEnabled = false)
    {
        return store.Upsert(records, keepExistingStatus, keepExistingEnabled, TestCt);
    }

    private async Task<ProxyEndPointStore> CreateStore(params ProxyEndPoint[] proxyEndPoints)
    {
        var store = new ProxyEndPointStore(Path.Combine(TestHelper.WorkingPath, "proxies", "proxies.db"));
        if (proxyEndPoints.Length > 0)
            await Upsert(store, proxyEndPoints.Select(x => new ProxyEndPointRecord { EndPoint = x }).ToArray());
        return store;
    }

    // the client derives the same db path from its storage folder
    private string ClientStoreDbPath =>
        Path.Combine(TestHelper.WorkingPath, "ClientCore", "proxies", "proxies.db");

    [TestMethod]
    public async Task IsEnabled_False_When_No_Servers()
    {
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), serverCheckTimeout: null, TestCt);
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task IsEnabled_True_When_Servers_Exist()
    {
        var store = await CreateStore(
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, serverCheckTimeout: null, TestCt);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), serverCheckTimeout: null, TestCt);
        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(async () =>
            await mgr.ConnectAsync(socketFactory, target, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt));

        Assert.AreEqual((int)SocketError.NetworkUnreachable, ex.ErrorCode);
    }

    [TestMethod]
    public async Task Constructor_Supports_Multiple_Proxy_Types()
    {
        var store = await CreateStore(
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "127.0.0.1", Port = 8080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Https, Host = "127.0.0.1", Port = 8443 });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, serverCheckTimeout: null, TestCt);

        Assert.IsTrue(mgr.IsEnabled);
        Assert.HasCount(4, mgr.GetEndPointInfos());
    }

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

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, serverCheckTimeout: null, TestCt);
        await mgr.CheckServers(socketFactory, new IPEndPoint(IPAddress.Loopback, 443), hostName: null, serverCertificateValidation: null, TestCt);

        // All servers that are no SOCKS4 proxy should be marked as inactive
        Assert.IsTrue(mgr.GetEndPointInfos().All(status => !status.EndPoint.IsEnabled));
    }

    // a connector whose one proxy has a status its store has not saved yet: a connect through it succeeded
    private async Task<(ManagedProxyConnector Connector, TestProxyEndPointStore Store)> CreateConnectorWithUnsavedStatus()
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = new TestProxyEndPointStore(await CreateStore(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5,
            Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
            Port = socks5ProxyServer.ListenerEndPoint.Port
        }));
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store, serverCheckTimeout: null, TestCt);
        using var connection = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        return (connector, store);
    }

    [TestMethod]
    public async Task A_second_close_waits_for_the_first_ones_flush()
    {
        var (connector, store) = await CreateConnectorWithUnsavedStatus();

        // the first close's flush is held
        store.HoldUpdates = true;
        var firstClose = connector.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => Volatile.Read(ref store.HeldUpdateCount),
            "The first close should flush.", cancellationToken: TestCt);

        var secondClose = connector.DisposeAsync().AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestCt);
        Assert.IsFalse(secondClose.IsCompleted);

        store.ReleaseUpdates();
        await Task.WhenAll(firstClose, secondClose).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsTrue(store.IsDisposed);
    }

    [TestMethod]
    public async Task A_failed_flush_still_closes_the_store()
    {
        var (connector, store) = await CreateConnectorWithUnsavedStatus();

        store.FailUpdates = true;
        await connector.DisposeAsync();
        Assert.IsTrue(store.IsDisposed);
    }

    // a connection through the connector's one proxy, whose status says it worked last, so the connect checks no
    // server first
    private async Task<(ManagedProxyConnector Connector, TcpClient TcpClient)> ConnectThroughProxy(
        TimeSpan? serverCheckTimeout = null, Func<IProxyEndPointStore, IProxyEndPointStore>? wrapStore = null)
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Socks5,
                    Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = socks5ProxyServer.ListenerEndPoint.Port
                },
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);
        var connector = await ManagedProxyConnector.Create(new ProxyOptions(), wrapStore?.Invoke(store) ?? store,
            serverCheckTimeout, TestCt);

        var connection = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        return (connector, connection.TcpClient);
    }

    [TestMethod]
    public async Task A_failure_after_the_connect_counts_once_against_the_proxy_that_made_the_connection()
    {
        var connection = await ConnectThroughProxy();
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        var status = connector.GetEndPointInfos().Single().Status;
        var succeededCount = status.SucceededCount;
        var failedCount = status.FailedCount;

        // as a TLS handshake's, reported twice
        var error = new AuthenticationException("The TLS handshake failed.");
        connector.RecordFailed(tcpClient, error);
        connector.RecordFailed(tcpClient, error);

        Assert.AreEqual(succeededCount - 1, status.SucceededCount);
        Assert.AreEqual(failedCount + 1, status.FailedCount);
        Assert.AreEqual(error.Message, status.ErrorMessage);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_cancellation_after_the_connect_counts_against_the_proxy_only_past_a_server_checks_time(
        bool isPastCheckTime)
    {
        var serverCheckTimeout = isPastCheckTime ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(30);
        var connection = await ConnectThroughProxy(serverCheckTimeout);
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        var status = connector.GetEndPointInfos().Single().Status;
        var failedCount = status.FailedCount;

        if (isPastCheckTime)
            await Task.Delay(serverCheckTimeout * 2, TestCt);
        connector.RecordFailed(tcpClient, new OperationCanceledException());

        Assert.AreEqual(isPastCheckTime ? failedCount + 1 : failedCount, status.FailedCount);
    }

    [TestMethod]
    public async Task A_failure_after_a_cancellation_of_the_same_connection_still_counts()
    {
        // the cancellation forgot the connection, and the failure that came after it found none
        var connection = await ConnectThroughProxy();
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        var status = connector.GetEndPointInfos().Single().Status;
        var failedCount = status.FailedCount;

        connector.RecordFailed(tcpClient, new OperationCanceledException());
        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));

        Assert.AreEqual(failedCount + 1, status.FailedCount);
    }

    [TestMethod]
    public async Task A_change_made_while_a_flush_writes_is_saved_by_the_next_one()
    {
        TestProxyEndPointStore? store = null;
        var connection = await ConnectThroughProxy(wrapStore: x => store = new TestProxyEndPointStore(x));
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        Assert.IsNotNull(store);

        // the connection fails after the flush wrote, before it ended: the flags it cleared at its end took the change
        store.HoldAfterUpdates = true;
        var flushTask = connector.Flush(TestCt);
        await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldUpdateCount), "The flush should write.");
        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));
        store.ReleaseUpdates();
        await flushTask;

        await connector.Flush(TestCt);
        var record = await store.Get(connector.GetEndPointInfos().Single().EndPoint.Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual(1, record.Status.FailedCount);
    }

    // an endpoint of the test SOCKS5 proxy
    private static ProxyEndPoint ToProxyEndPoint(IPEndPoint endPoint, bool isEnabled = true)
    {
        return new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5,
            Host = endPoint.Address.ToString(),
            Port = endPoint.Port,
            IsEnabled = isEnabled
        };
    }

    // the name a TLS client gave at its handshake with the first connection the listener takes
    private async Task<string> AcceptTls(TcpListener listener, X509Certificate2 certificate)
    {
        using var client = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(client.GetStream());
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);

        return sslStream.TargetHostName;
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
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), serverCheckTimeout: null, TestCt);

        var vpnServerEndPoint = (IPEndPoint)vpnServer.LocalEndpoint;
        await connector.CheckServers(new TestSocketFactory(), vpnServerEndPoint, "vpn.example", serverCertificateValidation: null, TestCt);

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
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)vpnServer.LocalEndpoint, hostName: null, serverCertificateValidation: null,
            TestCt);

        var info = connector.GetEndPointInfos().Single();
        Assert.IsTrue(info.Status.IsLastUsedFailed);
        Assert.IsTrue(info.EndPoint.IsEnabled);
    }

    [TestMethod]
    public async Task An_endpoint_never_used_is_no_success()
    {
        // one never used counted as a success, a disabled one too, so the client blamed the server for the proxies'
        // failure
        using var closedPort = new TcpListener(IPAddress.Loopback, 0);
        closedPort.Start();
        var refusingEndPoint = (IPEndPoint)closedPort.LocalEndpoint;
        closedPort.Stop();

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(
                ToProxyEndPoint(refusingEndPoint),
                ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1), isEnabled: false)),
            serverCheckTimeout: null, TestCt);

        Assert.IsFalse(connector.Status.IsAnySucceeded, "None was used.");

        await connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443), hostName: null, serverCertificateValidation: null,
            TestCt);

        Assert.IsFalse(connector.Status.IsAnySucceeded);
        Assert.AreEqual(1, connector.Status.FailedServerCount);
    }

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
    public async Task A_canceled_check_keeps_what_it_found_and_closes_its_sockets()
    {
        // what the ended checks found was recorded after the run, which a cancellation left before it, and before the
        // sockets were closed
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy(standIn: (IPEndPoint)target.LocalEndpoint).Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var silentEndPoint = (IPEndPoint)silentProxy.LocalEndpoint;

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint(silentEndPoint)),
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var socketFactory = new RecordingSocketFactory();
        var checkTask = connector.CheckServers(socketFactory, (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, checkCts.Token);

        using var checkedConnection = await target.AcceptTcpClientAsync(TestCt);
        await AssertEqualsWait(1, () => connector.Progress?.Completed ?? 0, "The answering proxy's check should end.");
        await checkCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => checkTask);

        var infos = connector.GetEndPointInfos();
        Assert.AreEqual(1, infos.Single(x => x.EndPoint.Port == proxy.EndPoint.Port).Status.SucceededCount);
        Assert.AreEqual(0, infos.Single(x => x.EndPoint.Port == silentEndPoint.Port).Status.FailedCount,
            "A cancellation is no failure.");

        // every client the check made is closed, the one whose check ended too
        Assert.HasCount(2, socketFactory.TcpClients);
        foreach (var tcpClient in socketFactory.TcpClients)
            Assert.ThrowsExactly<ObjectDisposedException>(() => tcpClient.GetStream());
    }

    // the system's sockets, each TCP client it makes kept for the test to look at
    private sealed class RecordingSocketFactory : TestSocketFactory
    {
        public ConcurrentQueue<TcpClient> TcpClients { get; } = new();

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            var tcpClient = base.CreateTcpClient(ipEndPoint, options);
            TcpClients.Enqueue(tcpClient);
            return tcpClient;
        }
    }

    [TestMethod]
    public async Task A_canceled_check_keeps_the_failures_that_came_before_its_cancellation()
    {
        // a canceled run recorded no failure at all
        using var closedPort = new TcpListener(IPAddress.Loopback, 0);
        closedPort.Start();
        var refusingEndPoint = (IPEndPoint)closedPort.LocalEndpoint;
        closedPort.Stop();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var silentEndPoint = (IPEndPoint)silentProxy.LocalEndpoint;

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(refusingEndPoint), ToProxyEndPoint(silentEndPoint)),
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var checkTask = connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, checkCts.Token);

        await AssertEqualsWait(1, () => connector.Progress?.Completed ?? 0, "The refusing proxy's check should end.",
            timeout: 10000);

        await checkCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => checkTask);

        var infos = connector.GetEndPointInfos();
        Assert.AreEqual(1, infos.Single(x => x.EndPoint.Port == refusingEndPoint.Port).Status.FailedCount);
        Assert.AreEqual(0, infos.Single(x => x.EndPoint.Port == silentEndPoint.Port).Status.FailedCount);
    }

    [TestMethod]
    public async Task A_cancellation_during_the_last_attempt_is_thrown_as_one()
    {
        // it read as a failure of every proxy: no network
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions(), store,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var connectTask = connector.ConnectAsync(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, onAttempt: null, connectCts.Token);

        // the attempt is under way: the proxy has the client's greeting
        using var attempt = await silentProxy.AcceptTcpClientAsync(TestCt);
        await attempt.GetStream().ReadExactlyAsync(new byte[3], TestCt);
        await connectCts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => connectTask);
        Assert.AreEqual(0, connector.GetEndPointInfos().Single().Status.FailedCount);
    }

    [TestMethod]
    public async Task An_HTTPS_proxy_whose_certificate_is_not_trusted_is_not_used()
    {
        // any certificate was taken: whoever was in the path got the user name and the password
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var requestByteCount = 0;
        _ = ServeTlsCountingRequests(proxyListener, certificate, count => Interlocked.Add(ref requestByteCount, count));
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Https,
                    Host = IPAddress.Loopback.ToString(),
                    Port = ((IPEndPoint)proxyListener.LocalEndpoint).Port,
                    Username = "user",
                    Password = "secret"
                },
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions(), store,
            serverCheckTimeout: null, TestCt);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(() => connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null,
            TestCt));

        Assert.AreEqual((int)SocketError.NetworkUnreachable, ex.ErrorCode);
        var status = connector.GetEndPointInfos().Single().Status;
        Assert.IsTrue(status.IsLastUsedFailed);
        Assert.Contains("certificate", status.ErrorMessage ?? string.Empty);
        Assert.AreEqual(0, Volatile.Read(ref requestByteCount), "No request reached the proxy.");
    }

    // A TLS server that counts the bytes it reads after each handshake, a proxy's request and its credentials, and
    // answers a request as a proxy that opened the tunnel
    private async Task ServeTlsCountingRequests(TcpListener listener, X509Certificate2 certificate,
        Action<int> onRead)
    {
        while (true) {
            using var client = await listener.AcceptTcpClientAsync(TestCt);
            await using var sslStream = new SslStream(client.GetStream());
            try {
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);

                var buffer = new byte[4096];
                var read = await sslStream.ReadAsync(buffer, TestCt);
                onRead(read);
                await sslStream.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray(), TestCt);
                while ((read = await sslStream.ReadAsync(buffer, TestCt)) > 0)
                    onRead(read);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException) {
                // the client's refusal of the certificate, or its close
            }
        }
    }

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

    [TestMethod]
    public async Task Of_two_endpoints_that_never_failed_the_one_used_longer_ago_goes_first()
    {
        // their last use read as none, so the store's order took its place
        using var proxyA = new TestSocks5Proxy().Start();
        using var proxyB = new TestSocks5Proxy().Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = await CreateStore();
        var now = DateTime.UtcNow;
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxyB.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = now.AddMinutes(-1) }
            },
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxyA.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = now.AddMinutes(-10) }
            }
        ]);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions(), store,
            serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.HasCount(1, proxyA.Destinations);
        Assert.IsEmpty(proxyB.Destinations);
    }

    [TestMethod]
    public async Task A_flush_started_while_another_writes_saves_after_it()
    {
        // the two wrote at once, and the earlier one's older copy, written last, stood
        TestProxyEndPointStore? store = null;
        var connection = await ConnectThroughProxy(wrapStore: x => store = new TestProxyEndPointStore(x));
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        Assert.IsNotNull(store);

        // the first flush is held before its write, and the connection fails meanwhile
        store.HoldNextUpdate();
        var firstFlush = connector.Flush(TestCt);
        await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldUpdateCount), "The first flush should write.");
        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));
        var secondFlush = connector.Flush(TestCt);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestCt);
        store.ReleaseUpdates();
        await Task.WhenAll(firstFlush, secondFlush).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        var record = await store.Get(connector.GetEndPointInfos().Single().EndPoint.Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual(1, record.Status.FailedCount);
    }

    [TestMethod]
    public async Task A_failure_after_a_reload_counts_on_the_endpoint_as_reloaded()
    {
        // it counted on the object the reload replaced, which is saved no more
        var connection = await ConnectThroughProxy();
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;

        await connector.UpdateOptions(new ProxyOptions(), TestCt);
        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));

        Assert.AreEqual(1, connector.GetEndPointInfos().Single().Status.FailedCount);
    }

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

    [TestMethod]
    public async Task The_VPN_connection_goes_through_the_stream_its_proxy_connection_hands_out()
    {
        // the socket's own stream went around an HTTPS proxy's TLS session
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverTask = AcceptTls(listener, certificate);

        await using var connector = new StreamCountingProxyConnector();
        var vpnEndPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost",
            CertificateHash: null, PathBase: null);

        using var factory = new TcpStreamConnectionFactory(new TestSocketFactory(), connector, vpnEndPoint,
            (_, _, _, _) => true, tcpPacketChannelKernelBufferSize: null);

        await using var connection = await factory.CreateConnection("proxy-stream-test", isTcpPacketChannel: false,
            onConnectAttempt: null, TestCt);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsGreaterThan(0, connector.WrittenByteCount);
    }

    // a connector with no proxy: it connects straight to the server and hands out the socket's stream wrapped, so a
    // test sees what goes through the stream it hands out
    private sealed class StreamCountingProxyConnector : IProxyConnector
    {
        private int _writtenByteCount;
        public int WrittenByteCount => Volatile.Read(ref _writtenByteCount);
        public bool IsEnabled => true;
        public bool UseRecentSucceeded { get; set; }
        public ProgressStatus? Progress => null;

        public ProxyConnectorStatus Status => new() {
            AutoUpdate = false, SessionStatus = new ProxySessionStatus(), IsAnySucceeded = true,
            SucceededServerCount = 1, FailedServerCount = 0, UnknownServerCount = 0, DisabledServerCount = 0
        };

        public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
            string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
            CancellationToken cancellationToken)
        {
            var tcpClient = socketFactory.CreateTcpClient(ipEndPoint);
            await tcpClient.ConnectAsync(ipEndPoint, cancellationToken);
            return new ProxyConnection(tcpClient, new CountingStream(tcpClient.GetStream(),
                count => Interlocked.Add(ref _writtenByteCount, count)));
        }

        public void RecordFailed(TcpClient tcpClient, Exception ex) { }

        public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
            RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Flush(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    // a stream that counts the bytes written through it
    private sealed class CountingStream(Stream inner, Action<int> onWrite) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            onWrite(count);
            inner.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onWrite(buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [TestMethod]
    public async Task A_session_runs_through_an_HTTPS_proxy()
    {
        // the tunnel went around the proxy's TLS session, so no HTTPS proxy carried one
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var httpsProxyServer = new HttpsProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate
        });

        httpsProxyServer.Start();

        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var connector = new TestHttpsProxyConnector(httpsProxyServer.ListenerEndPoint);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), proxyConnector: connector);

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsGreaterThan(0, connector.ConnectionCount);
    }

    // a connector of one HTTPS proxy whose certificate it takes, as the connectors take a trusted one, and whose
    // connection it hands out as theirs: through the factory's choice of its stream
    private sealed class TestHttpsProxyConnector(IPEndPoint proxyEndPoint) : IProxyConnector
    {
        private int _connectionCount;
        public int ConnectionCount => Volatile.Read(ref _connectionCount);
        public bool IsEnabled => true;
        public bool UseRecentSucceeded { get; set; }
        public ProgressStatus? Progress => null;

        public ProxyConnectorStatus Status => new() {
            AutoUpdate = false, SessionStatus = new ProxySessionStatus(), IsAnySucceeded = true,
            SucceededServerCount = 1, FailedServerCount = 0, UnknownServerCount = 0, DisabledServerCount = 0
        };

        public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
            string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
            CancellationToken cancellationToken)
        {
            var proxyClient = new VpnHood.Net.Proxies.HttpProxyClients.HttpProxyClient(
                new VpnHood.Net.Proxies.HttpProxyClients.HttpProxyClientOptions {
                    ProxyEndPoint = proxyEndPoint,
                    UseTls = true,
                    AllowInvalidCertificates = true
                });

            var tcpClient = socketFactory.CreateTcpClient(proxyEndPoint);
            await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken);
            Interlocked.Increment(ref _connectionCount);
            return ProxyClientFactory.GetConnection(proxyClient, tcpClient);
        }

        public void RecordFailed(TcpClient tcpClient, Exception ex) { }

        public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
            RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Flush(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
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

    [TestMethod]
    public async Task A_proxy_that_refuses_its_credentials_is_reported_as_the_proxys_failure()
    {
        // its refusal read as the VPN server's answer of 401, so the server was taken for reachable
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Username = "user",
            Password = "secret"
        });

        socks5ProxyServer.Start();

        var proxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint);
        proxyEndPoint.Username = "user";
        proxyEndPoint.Password = "wrong";
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Simple, ProxyEndPoint = proxyEndPoint }));
    }

    [TestMethod]
    public async Task A_server_that_is_down_behind_a_working_proxy_is_reported_as_the_servers_failure()
    {
        // with the check against the server, no proxy passed, and the error named the proxies
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();

        // a key whose server is gone: the proxy answers that it refused
        Token token;
        await using (var server = await TestHelper.CreateServer())
            token = await TestHelper.CreateAccessToken(server);

        await Assert.ThrowsExactlyAsync<UnreachableServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple, ProxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint)
            }));
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
            }), serverCheckTimeout: null, TestCt);

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
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), serverCheckTimeout: null, TestCt);

        var serverHash = serverCertificate.GetCertHash();
        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)vpnServer.LocalEndpoint, "vpn.example",
            (_, certificate, _, _) => certificate?.GetCertHash().SequenceEqual(serverHash) == true, TestCt);

        var info = connector.GetEndPointInfos().Single();
        Assert.IsTrue(info.Status.IsLastUsedFailed);
        Assert.IsTrue(info.EndPoint.IsEnabled);
    }

    [TestMethod]
    public async Task A_server_that_is_down_holds_no_connection_to_another_server()
    {
        // one check at a time for all servers: the down one's, which takes the check's whole time, held the others
        using var proxy = new TestSocks5Proxy().Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var downServer = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers no TLS
        downServer.Start();
        using var upServer = new TcpListener(IPAddress.Loopback, 0);
        upServer.Start();
        _ = AcceptTls(upServer, certificate);

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = true },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint)),
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        using var downCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var downConnect = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)downServer.LocalEndpoint,
            "down.example", serverCertificateValidation: null, onAttempt: null, downCts.Token);

        using var downProbe = await downServer.AcceptTcpClientAsync(TestCt); // its check is under way

        using var connection = await connector.ConnectAsync(new TestSocketFactory(),
                (IPEndPoint)upServer.LocalEndpoint, "up.example", serverCertificateValidation: null, onAttempt: null,
                TestCt)
            .WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        Assert.IsFalse(downConnect.IsCompleted);

        await downCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => downConnect);
    }

    [TestMethod]
    public async Task A_session_through_a_proxy_takes_TCP_alone()
    {
        // UDP and QUIC went to the server's address straight, around the proxy
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();

        var clientOptions = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple,
                ProxyEndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint)
            });

        var session = clientServerDom.Client.RequiredSession;
        Assert.IsNull(session.Config.HostUdpEndPoint);
        Assert.IsNull(session.Config.HostQuicEndPoint);
        CollectionAssert.AreEqual(new[] { ChannelProtocol.Tcp }, session.Info.ChannelProtocols.ToArray());
        Assert.AreEqual(ChannelProtocol.Tcp, session.Status.ChannelProtocol);
        await TestHelper.Test_Https();
    }

    [TestMethod]
    public async Task Statuses_Persist_Across_Manager_Instances()
    {
        // create a local SOCKS5 proxy
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();

        var proxyEndPoint = new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5,
            Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
            Port = socks5ProxyServer.ListenerEndPoint.Port
        };

        var socketFactory = new TestSocketFactory();
        var proxyOptions = new ProxyOptions { VerifyTls = false };
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();

        // first manager records a successful check and flushes on dispose
        var mgr = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(proxyEndPoint), serverCheckTimeout: null, TestCt);
        await mgr.CheckServers(socketFactory, (IPEndPoint)server.LocalEndpoint, hostName: null, serverCertificateValidation: null, TestCt);
        Assert.IsTrue(mgr.GetEndPointInfos().Single().Status.SucceededCount >= 1);
        await mgr.DisposeAsync();

        // a new manager over the same db restores the statuses
        var mgr2 = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(), serverCheckTimeout: null, TestCt);
        var restored = mgr2.GetEndPointInfos().Single();
        Assert.AreEqual(proxyEndPoint.Id, restored.EndPoint.Id);
        Assert.IsTrue(restored.Status.SucceededCount >= 1);
        Assert.IsNotNull(restored.Status.LastSucceeded);
        await mgr2.DisposeAsync();
    }

    [TestMethod]
    public async Task Connect_via_proxy()
    {
        // create a local SOCKS5 proxy using Socks5ProxyServer
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();

        // create a local HTTP proxy using HttpProxyServer
        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();

        // seed the shared endpoint store the client will open
        using (var store = new ProxyEndPointStore(ClientStoreDbPath)) {
            await Upsert(store, [
                new ProxyEndPointRecord {
                    EndPoint = new ProxyEndPoint {
                        Protocol = ProxyProtocol.Socks5,
                        Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                        Port = socks5ProxyServer.ListenerEndPoint.Port
                    }
                },
                new ProxyEndPointRecord {
                    EndPoint = new ProxyEndPoint {
                        Protocol = ProxyProtocol.Http,
                        Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
                        Port = httpProxyServer.ListenerEndPoint.Port
                    }
                }
            ]);
        }

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowChannelReuse = false; // make sure not reuse connections
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Managed });

        await TestHelper.Test_Https();

        var mgr = clientServerDom.Client.ProxyConnector as ManagedProxyConnector;
        Assert.IsNotNull(mgr);
        var proxyEndPointInfos = mgr.GetEndPointInfos();
        Assert.IsTrue(proxyEndPointInfos.All(x => x.Status.SucceededCount >= 1));
    }

    [TestMethod]
    public async Task Connect_via_single_proxy()
    {
        // create a local SOCKS5 proxy using Socks5ProxyServer
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowChannelReuse = false; // make sure not reuse connections
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions {
                Mode = ProxyMode.Simple,
                ProxyEndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Socks5,
                    Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = socks5ProxyServer.ListenerEndPoint.Port
                }
            });

        await TestHelper.Test_Https();

        // the lightweight path must not create the shared endpoint db
        var connector = clientServerDom.Client.ProxyConnector as SimpleProxyConnector;
        Assert.IsNotNull(connector);
        Assert.IsFalse(File.Exists(ClientStoreDbPath));
        var status = connector.Status;
        Assert.IsTrue(status.SessionStatus.SucceededCount >= 1);
    }

    // The list's auto-update downloads at once when it starts, and again only for a new url or interval: the
    // options an app sends again unchanged, at every settings save, download nothing.
    [TestMethod]
    public async Task Auto_update_downloads_again_only_for_a_new_url_or_interval()
    {
        // a list server on a free port, counting each path's downloads
        var downloads = new ConcurrentDictionary<string, int>();
        var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start();
        var port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
        portFinder.Stop();
        var webServer = new WebserverLite(new WebserverSettings(IPAddress.Loopback.ToString(), port), async ctx => {
            downloads.AddOrUpdate(ctx.Request.Url.RawWithoutQuery, 1, (_, count) => count + 1);
            await ctx.Response.Send("socks5://127.0.0.1:1080");
        });
        _ = webServer.StartAsync(TestCt);
        try {
            await using var connector =
                await ManagedProxyConnector.Create(CreateAutoUpdateOptions(port, "/a"), await CreateStore(), serverCheckTimeout: null, TestCt);
            await AssertEqualsWait(1, () => downloads.GetValueOrDefault("/a"), "the first download");

            // the same options again: no download
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/a"), TestCt);
            await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
            Assert.AreEqual(1, downloads.GetValueOrDefault("/a"), "no download for the same options");

            // a new url: a download at once
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/b"), TestCt);
            await AssertEqualsWait(1, () => downloads.GetValueOrDefault("/b"), "a download for the new url");
        }
        finally {
            webServer.TryStop();
        }
    }

    // nine seconds: under the slow job runner's ten, so the fast one's two-second beat starts the job
    private static ProxyOptions CreateAutoUpdateOptions(int port, string path)
    {
        return new ProxyOptions {
            AutoUpdateOptions = new ProxyAutoUpdateOptions {
                Url = new Uri($"http://127.0.0.1:{port}{path}"),
                Interval = TimeSpan.FromSeconds(9)
            }
        };
    }
}
