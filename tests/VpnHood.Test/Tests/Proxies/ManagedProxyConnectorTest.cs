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
public class ManagedProxyConnectorTest : ProxyTestBase
{
    [TestMethod]
    public async Task IsEnabled_False_When_No_Servers()
    {
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), socketFactory: null,
            serverCheckTimeout: null, TestCt);

        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task IsEnabled_True_When_Servers_Exist()
    {
        var store = await CreateStore(
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), socketFactory: null,
            serverCheckTimeout: null, TestCt);

        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(async () =>
            await mgr.ConnectAsync(socketFactory, target, hostName: null, serverCertificateValidation: null,
                onAttempt: null, TestCt));

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

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        Assert.IsTrue(mgr.IsEnabled);
        Assert.HasCount(4, mgr.GetEndPointInfos());
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
            socketFactory: null, serverCheckTimeout: null, TestCt);

        Assert.IsFalse(connector.Status.IsAnySucceeded, "None was used.");

        await connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443), hostName: null,
            serverCertificateValidation: null, TestCt);

        Assert.IsFalse(connector.Status.IsAnySucceeded);
        Assert.AreEqual(1, connector.Status.FailedServerCount);
    }

    // A connection through a proxy that succeeded last checks nothing, and the close left its dial running: it then
    // handed out a connection the closed connector made after its save. The close now stops the dial, which records
    // nothing
    [TestMethod]
    public async Task The_close_stops_a_connections_dial_which_hands_out_nothing()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The connection did not reach the proxy.");
        try {
            await connector.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);

            // the dial ends with the close, while the proxy still holds its answer
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                connecting.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        }
        finally {
            release.TrySetResult();
        }

        Assert.AreEqual(1, connector.GetEndPointInfos().Single().Status.SucceededCount,
            "The dial the close stopped was recorded.");
    }

    // Two connections at once, each through one of two endpoints of a host: the first's success turned the second off,
    // and the second's then turned the first off, so none of the host's was left. A success turns off the others only
    // where its own endpoint is still on
    [TestMethod]
    public async Task Two_connections_through_duplicates_at_once_leave_one_of_them_on()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstProxy = new TestSocks5Proxy { ReplyGate = firstRelease.Task }.Start();
        using var secondProxy = new TestSocks5Proxy { ReplyGate = secondRelease.Task }.Start();

        // both succeeded last, on this machine's address; the second off, so the first connection takes the first
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(firstProxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(secondProxy.EndPoint, isEnabled: false),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = true }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        try {
            var firstConnecting = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
                serverCertificateValidation: null, onAttempt: null, TestCt);

            await AssertEqualsWait(1, () => firstProxy.Destinations.Count, "The first connection did not dial.");

            // the second on and the first off, so the second connection takes the second; then both on, as both dial
            using var appStore = await CreateStore();
            await Upsert(appStore, [
                new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(firstProxy.EndPoint, isEnabled: false) },
                new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(secondProxy.EndPoint) }
            ]);

            await connector.UpdateOptions(proxyOptions, TestCt);
            var secondConnecting = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
                serverCertificateValidation: null, onAttempt: null, TestCt);

            await AssertEqualsWait(1, () => secondProxy.Destinations.Count, "The second connection did not dial.");
            await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(firstProxy.EndPoint) }]);
            await connector.UpdateOptions(proxyOptions, TestCt);

            firstRelease.TrySetResult();
            using var firstConnection = await firstConnecting;
            secondRelease.TrySetResult();
            using var secondConnection = await secondConnecting;

            Assert.AreEqual(1, connector.GetEndPointInfos().Count(x => x.EndPoint.IsEnabled),
                "Not one endpoint of the host was left on.");
        }
        finally {
            firstRelease.TrySetResult();
            secondRelease.TrySetResult();
        }
    }

    // A connection limited to the endpoints that succeeded lately, as the server search's probes are, took its list
    // once: where those all failed, it failed, though the check behind the connect had passed another meanwhile. It now
    // tries those before it fails. Both are on this machine's address: with one endpoint per address, the one it then
    // takes turns off the first, which its retry left on, as it looked for duplicates among the retry's alone
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_connection_tries_an_endpoint_that_passed_after_it_took_its_list(bool removeDuplicateIps)
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        var firstProxy = new TestSocks5Proxy().Start();
        var firstEndPoint = firstProxy.EndPoint;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = removeDuplicateIps }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(firstEndPoint), ToProxyEndPoint(heldProxy.EndPoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        await connector.CheckServers(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        // the proxy that passed goes down, and the held one passes while the connection dials the first
        firstProxy.Dispose();
        connector.UseRecentSucceeded = true;
        var heldStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == heldProxy.EndPoint.Port).Status;
        var socketFactory = new DialHookSocketFactory(endPoint => {
            if (endPoint.Port != firstEndPoint.Port)
                return;

            release.TrySetResult();
            SpinWait.SpinUntil(() => heldStatus.IsLastUsedSucceeded, TimeSpan.FromSeconds(10));
        });

        using var connection = await connector.ConnectAsync(socketFactory, targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.AreEqual(heldProxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        Assert.AreEqual(!removeDuplicateIps,
            connector.GetEndPointInfos().Single(x => x.EndPoint.Port == firstEndPoint.Port).EndPoint.IsEnabled,
            "The duplicate of the endpoint the connection took is not as the rule of one endpoint per address has it.");
    }

    // A connection dialed every endpoint of the list it took, one the user turned off meanwhile too
    [TestMethod]
    public async Task A_connection_skips_an_endpoint_turned_off_after_it_took_its_list()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var silentProxy = new SilentProxy().Start();
        using var proxy = new TestSocks5Proxy().Start();

        // both succeeded last, the silent one first in turn
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(silentProxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus {
                    SucceededCount = 1, LastSucceeded = DateTime.UtcNow, QueuePosition = 100
                }
            }
        ]);

        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        // the user turns the second off while the connection dials the first, which then fails
        using var silentConnection = await silentProxy.TakeConnection(TestCt);
        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy.EndPoint, isEnabled: false) }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        silentConnection.Dispose();

        await Assert.ThrowsExactlyAsync<SocketException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(10), TestCt));
        Assert.IsEmpty(proxy.Destinations, "The connection dialed an endpoint turned off.");
    }

    // A cancellation during the last attempt read as a failure of every proxy: no network. Given a check time with no
    // end, every cancellation counted against the proxy, as none came within that time
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_cancellation_during_the_last_attempt_is_thrown_as_one(bool isCheckTimeEndless)
    {
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null,
            serverCheckTimeout: isCheckTimeEndless ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30), TestCt);

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
            socketFactory: null, serverCheckTimeout: null, TestCt);

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
            socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.HasCount(1, proxyA.Destinations);
        Assert.IsEmpty(proxyB.Destinations);
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
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

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

    // A pass ends a check, so a proxy whose check a pass stopped stayed unrated, first in turn, and every connection
    // checked the list again before its own connect: each one reached the server twice through the fast proxy, once for
    // the check. A connection now checks only where none it may take succeeded last. The slow proxy never answers, so
    // the fast one passes first whatever the timing
    [TestMethod]
    public async Task A_connection_checks_nothing_while_a_proxy_it_may_take_succeeded_last()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var fastRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fastProxy = new TestSocks5Proxy { ReplyGate = fastRelease.Task }.Start();
        var neverReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var slowProxy = new TestSocks5Proxy { ReplyGate = neverReleased.Task }.Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to the fast one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(fastProxy.EndPoint), ToProxyEndPoint(slowProxy.EndPoint)),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        // the first connection's check passes at the fast proxy once it dialed the slow one too
        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => slowProxy.Destinations.Count,
            "The first connection's check missed the slow one.");

        fastRelease.TrySetResult();
        using (await connecting) {
            // the first connection
        }

        for (var i = 0; i < 9; i++) {
            using var connection = await connector.ConnectAsync(new TestSocketFactory(),
                (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null,
                TestCt);
        }

        // The first connection's check, then the ten connections. The check of the rest behind its pass takes the slow
        // proxy alone, whose check that pass stopped: it dialed the fast one too, beside the connection's own dial
        await AssertEqualsWait(2, () => slowProxy.Destinations.Count, "The rest was not checked behind the pass.");
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestCt);
        Assert.AreEqual(11, fastProxy.Destinations.Count, "Connections or the check behind them dialed it again.");
    }
}
