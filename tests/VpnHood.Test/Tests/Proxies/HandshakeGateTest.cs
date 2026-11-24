using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Checks;
using VpnHood.Core.Server.Access;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class HandshakeGateTest : ProxyTestBase
{
    // On iOS and tvOS the check behind a connect dialed 3 at a time beside the session, so a list whose proxies mostly
    // never answer took minutes to rate, each holding its place for the check's whole time. It now dials as many at
    // once as before its pass, as only its TLS handshakes cost an extension's memory: here two, the run's width, where
    // one handshake at a time is the width beside the session; and one whose check ends gives its place to the next
    [TestMethod]
    public async Task The_check_behind_a_connect_dials_as_many_at_once_as_before_its_pass()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var silentProxies = Enumerable.Range(0, 3).Select(_ => new SilentProxy().Start()).ToArray();
        try {
            await using var connector =
                await CreateConnectorCheckingBehindItsPass((IPEndPoint)target.LocalEndpoint, silentProxies);

            await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
            Assert.AreEqual(2, silentProxies.Sum(x => x.ConnectionCount), "The check dialed more than two at once.");

            var dialed = silentProxies.First(x => x.ConnectionCount == 1);
            (await dialed.TakeConnection(TestCt)).Dispose();
            await AssertEqualsWait(3, () => silentProxies.Sum(x => x.ConnectionCount),
                "A check that ended gave its place to no other.");
        }
        finally {
            foreach (var silentProxy in silentProxies)
                silentProxy.Dispose();
        }
    }

    // A check that waited for its place behind the connect held the endpoint it took before the wait, and dialed it
    // after, however long that was, though the user had turned it off meanwhile
    [TestMethod]
    public async Task A_check_waiting_for_its_place_skips_an_endpoint_turned_off_meanwhile()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var silentProxies = Enumerable.Range(0, 3).Select(_ => new SilentProxy().Start()).ToArray();
        try {
            await using var connector =
                await CreateConnectorCheckingBehindItsPass((IPEndPoint)target.LocalEndpoint, silentProxies);

            // the user turns off the one that waits for a place, which the dialed ones' failures then free
            var waiting = silentProxies.Single(x => x.ConnectionCount == 0);
            using var appStore = await CreateStore();
            await Upsert(appStore,
                [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(waiting.EndPoint, isEnabled: false) }]);

            await connector.UpdateOptions(new ProxyOptions { VerifyTls = false }, TestCt);
            foreach (var dialed in silentProxies.Where(x => x != waiting))
                (await dialed.TakeConnection(TestCt)).Dispose();

            // to its end: a dial of an endpoint turned off would hold it for its check's time
            await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
                "The check did not end: it dialed an endpoint turned off while it waited for its place.");

            Assert.AreEqual(0, waiting.ConnectionCount,
                "A check dialed an endpoint turned off while it waited for its place.");
        }
        finally {
            foreach (var silentProxy in silentProxies)
                silentProxy.Dispose();
        }
    }

    // A connector whose check before a connect passed at two answering proxies, which took the run's two places at
    // once, and goes on through the silent ones behind the connect, two of them dialed and the third waiting for a
    // place: its width is two, and one TLS handshake at a time beside the session
    private async Task<ManagedProxyConnector> CreateConnectorCheckingBehindItsPass(IPEndPoint targetEndPoint,
        IReadOnlyList<SilentProxy> silentProxies)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy1 = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        using var proxy2 = new TestSocks5Proxy { ReplyGate = release.Task }.Start();

        // the two answering proxies succeeded last, so they take the run's two places first; the silent ones failed
        var store = await CreateStore();
        await Upsert(store, [
            .. new[] { proxy1.EndPoint, proxy2.EndPoint }.Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }),
            .. silentProxies.Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            })
        ]);

        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        try {
            connector.CheckWidth = 2;
            connector.HandshakePlaces = new SemaphoreSlim(1);
            var checkTask = connector.CheckServers(new TestSocketFactory(), targetEndPoint, hostName: null,
                serverCertificateValidation: null, TestCt);

            await AssertEqualsWait(2, () => proxy1.Destinations.Count + proxy2.Destinations.Count,
                "The check dialed fewer at once than its width before its first pass.");

            release.TrySetResult();
            await checkTask;
            await AssertEqualsWait(2, () => silentProxies.Sum(x => x.ConnectionCount),
                "The check behind the connect dialed fewer at once than before its pass.");

            await AssertEqualsWait(true, () => connector.Progress?.Completed >= 2, "The two passes were not recorded.");
            return connector;
        }
        catch {
            await connector.DisposeAsync();
            throw;
        }
    }

    // On iOS and tvOS a check beside the session made its TLS handshakes as many at once as it dialed, each costing
    // memory an extension has little of. They now take turns, an HTTPS proxy's own and the server's through a SOCKS
    // proxy alike: here one at a time, the second when the first ends
    [TestMethod]
    public async Task The_checks_beside_the_session_make_one_TLS_handshake_at_a_time()
    {
        // a VPN server and an HTTPS proxy that answer no TLS hello
        using var vpnServer = new SilentProxy().Start();
        using var httpsProxy = new SilentProxy().Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var socksProxy = new TestSocks5Proxy().Start();
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var connection = await connector.ConnectAsync(new TestSocketFactory(), vpnServer.EndPoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        // the two the app adds get a check behind the session
        using var appStore = await CreateStore(
            new ProxyEndPoint {
                Protocol = ProxyProtocol.Https,
                Host = httpsProxy.EndPoint.Address.ToString(),
                Port = httpsProxy.EndPoint.Port
            },
            ToProxyEndPoint(socksProxy.EndPoint));

        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        await AssertEqualsWait(1, () => httpsProxy.HelloCount + vpnServer.HelloCount, "No handshake began.");
        await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
        Assert.AreEqual(1, httpsProxy.HelloCount + vpnServer.HelloCount, "The checks made two handshakes at once.");

        var first = httpsProxy.HelloCount == 1 ? httpsProxy : vpnServer;
        var second = first == httpsProxy ? vpnServer : httpsProxy;
        first.CloseHelloConnections();
        await AssertEqualsWait(1, () => second.HelloCount, "The other handshake did not follow the first one's end.");
    }

    // With one TLS handshake at a time beside the session, a check that waited for its turn held a connection to the
    // server open, which the VPN server closes 10 seconds after its accept where no hello came, and a proxy that works
    // was rated failed. Such a check now waits with nothing open: here the first hello is held while the others wait
    [TestMethod]
    public async Task A_check_that_waits_for_its_turn_holds_no_connection_open()
    {
        using var vpnServer = new PausingTlsServer { HoldsFirst = true }.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var newProxies = Enumerable.Range(0, 3).Select(_ => new TestSocks5Proxy().Start()).ToArray();
        try {
            await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
            using var connection = await connector.ConnectAsync(new TestSocketFactory(), vpnServer.EndPoint,
                hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

            using var appStore = await CreateStore(newProxies.Select(x => ToProxyEndPoint(x.EndPoint)).ToArray());
            await connector.UpdateOptions(TlsCheckOptions, TestCt);

            // the first check's hello is held, and the dials of the two that wait have closed: the server has the
            // connection's connection and the first check's alone
            await AssertEqualsWait(1, () => vpnServer.HelloTimes.Count, "The first check's hello did not come.");
            await AssertEqualsWait(2, () => newProxies.Count(x => x.BareCloseCount >= 1),
                "Not every check that waits closed its dial.");

            await Task.Delay(TimeSpan.FromSeconds(0.5), TestCt);
            Assert.AreEqual(2, vpnServer.AcceptedCount, "A check that waits for its turn holds a connection open.");

            // at the proxies too: each waiting check's dial closed at once, and only the first check's is open
            Assert.AreEqual(1, newProxies.Sum(x => x.ConnectionCount - x.BareCloseCount),
                "A check that waits for its turn holds a connection to its proxy open.");

            vpnServer.ReleaseFirst();
            await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
                "The check of the added proxies did not end.", timeout: 30000);

            Assert.IsTrue(connector.GetEndPointInfos()
                    .Where(x => newProxies.Any(y => y.EndPoint.Port == x.EndPoint.Port))
                    .All(x => x.Status.IsLastUsedSucceeded),
                "A proxy that waited for its turn was rated failed.");
        }
        finally {
            foreach (var newProxy in newProxies)
                newProxy.Dispose();
        }
    }

    // With one TLS handshake at a time beside the session, a check whose turn came late ran out its time while it
    // waited, and a proxy that works was rated failed. Its time starts at its turn: here the test holds the only place
    // for longer than a check's time, then lets the checks through to a server that answers at once
    [TestMethod]
    public async Task A_check_whose_turn_comes_late_keeps_its_time()
    {
        using var vpnServer = new PausingTlsServer().Start();
        using var proxy = new TestSocks5Proxy().Start();
        var newProxies = Enumerable.Range(0, 3).Select(_ => new TestSocks5Proxy().Start()).ToArray();
        try {
            await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(2));
            using var connection = await connector.ConnectAsync(new TestSocketFactory(), vpnServer.EndPoint,
                hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

            var places = connector.HandshakePlaces ??
                         throw new InvalidOperationException("The connector has no places.");

            await places.WaitAsync(TestCt);
            using var appStore = await CreateStore(newProxies.Select(x => ToProxyEndPoint(x.EndPoint)).ToArray());
            await connector.UpdateOptions(TlsCheckOptions, TestCt);
            await AssertEqualsWait(true, () => newProxies.All(x => x.BareCloseCount >= 1),
                "Not every check's dial alone was closed.");

            // longer than a check's time, with no handshake made meanwhile
            await Task.Delay(TimeSpan.FromSeconds(2.5), TestCt);
            Assert.AreEqual(0, vpnServer.HelloTimes.Count, "A check made its handshake without its place.");
            Assert.IsTrue(connector.IsConnectCheckUnderWay, "The checks did not wait for their place.");

            places.Release();
            await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
                "The check of the added proxies did not end.", timeout: 30000);

            Assert.AreEqual(3, vpnServer.HelloTimes.Count, "Not every check made its handshake.");

            var failedPorts = connector.GetEndPointInfos()
                .Where(x => newProxies.Any(y => y.EndPoint.Port == x.EndPoint.Port) && !x.Status.IsLastUsedSucceeded)
                .Select(x => x.EndPoint.Port)
                .ToArray();

            Assert.AreEqual(0, failedPorts.Length,
                $"Proxies that waited for their turn were rated failed: {string.Join(", ", failedPorts)}.");
        }
        finally {
            foreach (var newProxy in newProxies)
                newProxy.Dispose();
        }
    }

    // A check that took its turn at the handshakes dialed its proxy alone and closed that connection before it took its
    // place, then dialed again, a round trip more where a place was free: every check of a key known by name alone did,
    // within the server search's few seconds, once the checks before a server took turns. It goes on through its dial
    // where a place is free, beside the session as before a server
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task A_check_that_finds_a_place_free_dials_its_proxy_once(bool isBesideSession)
    {
        using var vpnServer = new SilentProxy().Start();
        using var workingProxy = new TestSocks5Proxy().Start();
        using var proxy = new TestSocks5Proxy().Start();
        var socketFactory = new RecordingSocketFactory();
        await using var connector = await CreateConnectorWithAWorkingProxy(workingProxy, TimeSpan.FromSeconds(30));

        // beside the session, a connection gives the checks their server, and the proxy the app adds gets one; before
        // any proxy reached a server, the one that worked goes off, and a connection checks the added one itself
        using var connection = isBesideSession
            ? await connector.ConnectAsync(socketFactory, vpnServer.EndPoint, hostName: null,
                serverCertificateValidation: null, onAttempt: null, TestCt)
            : null;

        using var appStore = await CreateStore(ToProxyEndPoint(proxy.EndPoint),
            ToProxyEndPoint(workingProxy.EndPoint, isEnabled: isBesideSession));

        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var connecting = isBesideSession
            ? Task.CompletedTask
            : connector.ConnectAsync(socketFactory, vpnServer.EndPoint, hostName: null,
                serverCertificateValidation: null, onAttempt: null, connectCts.Token);

        await AssertEqualsWait(1, () => vpnServer.HelloCount, "The check made no handshake.");
        Assert.AreEqual(1, proxy.ConnectionCount, "The check dialed its proxy again though a place was free.");

        // its socket as the proxy client sets one it dials itself
        Assert.IsTrue(socketFactory.TcpClients.Last().NoDelay, "The check's socket delays what it sends.");

        await connectCts.CancelAsync();
        if (!isBesideSession)
            await Assert.ThrowsAsync<OperationCanceledException>(() => connecting);
    }

    // A SOCKS4 proxy carries no IPv6 address, which its client refuses before it dials, but a check that took its turn
    // at the handshakes dialed first and waited for a place: here none comes, and the check of an IPv6 server through
    // it never ended. It takes no turn, and fails at once
    [TestMethod]
    public async Task A_SOCKS4_check_of_an_IPv6_server_takes_no_turn()
    {
        using var socks4Proxy = new SilentProxy().Start();
        var store = await CreateStore(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks4,
            Host = socks4Proxy.EndPoint.Address.ToString(),
            Port = socks4Proxy.EndPoint.Port
        });

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        connector.SearchHandshakePlaces.Dispose();
        connector.SearchHandshakePlaces = new SemaphoreSlim(0);
        await Assert.ThrowsAsync<SocketException>(() => connector.ConnectAsync(new TestSocketFactory(),
            new IPEndPoint(IPAddress.IPv6Loopback, 443), hostName: null, serverCertificateValidation: null,
            onAttempt: null, TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt));

        // the check failed it, as did the connection's attempt after it, neither dialing it, and it stays on
        var endPointInfo = connector.GetEndPointInfos().Single();
        Assert.AreEqual(0, socks4Proxy.ConnectionCount, "A check dialed the proxy for an IPv6 server.");
        Assert.AreEqual(2, endPointInfo.Status.FailedCount, "The check did not fail the proxy.");
        Assert.IsTrue(endPointInfo.EndPoint.IsEnabled, "The proxy was turned off.");
    }

    // A connection's own check, before any proxy reached a server, as for a key known by name alone, runs within the
    // server search's short probe, which three handshakes that hang would outlast where they took turns at the
    // connections' places: it takes none of those
    [TestMethod]
    public async Task A_connections_own_check_takes_no_turns_at_the_connections_places_before_a_proxy_reached_a_server()
    {
        using var vpnServer = new SilentProxy().Start();
        using var proxy1 = new TestSocks5Proxy().Start();
        using var proxy2 = new TestSocks5Proxy().Start();
        var store = await CreateStore();
        await Upsert(store, new[] { proxy1, proxy2 }
            .Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            })
            .ToArray());

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        connector.HandshakePlaces = new SemaphoreSlim(1);
        connector.ConnectionHandshakePlaces = new SemaphoreSlim(1);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var connecting = connector.ConnectAsync(new TestSocketFactory(), vpnServer.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, connectCts.Token);

        await AssertEqualsWait(2, () => vpnServer.HelloCount,
            "The connection's own check took turns before any proxy reached a server.");

        await connectCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => connecting);
    }

    // Before any proxy reached a server, the server search's probe of each server checked with handshakes of its own,
    // 50 each, and up to 8 probes connected at once. The connections' own checks then share one check's width: here
    // two, both held by one server's checks, so the other server's wait for them. A check's end gives its place at once
    // to one that waits, while the other handshake and both connections go on
    [TestMethod]
    public async Task The_connections_own_checks_before_a_proxy_reached_a_server_share_one_checks_width()
    {
        using var vpnServer1 = new SilentProxy().Start();
        using var vpnServer2 = new SilentProxy().Start();
        using var proxy1 = new TestSocks5Proxy().Start();
        using var proxy2 = new TestSocks5Proxy().Start();
        TestSocks5Proxy[] proxies = [proxy1, proxy2];
        var store = await CreateStore();
        await Upsert(store, proxies
            .Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            })
            .ToArray());

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        connector.SearchHandshakePlaces.Dispose();
        connector.SearchHandshakePlaces = new SemaphoreSlim(2);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);

        // the first server's two checks hold both places, and the second server's two close their dials to wait
        var connecting1 = connector.ConnectAsync(new TestSocketFactory(), vpnServer1.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, connectCts.Token);

        await AssertEqualsWait(2, () => vpnServer1.HelloCount, "The first server's checks did not take both places.");
        var connecting2 = connector.ConnectAsync(new TestSocketFactory(), vpnServer2.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, connectCts.Token);

        await AssertEqualsWait(2, () => proxies.Sum(x => x.BareCloseCount),
            "The second server's checks did not wait for a place.");

        await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
        Assert.AreEqual(0, vpnServer2.HelloCount,
            "The second server's checks went around the places the first's hold.");

        // one of the first server's checks fails, as its server closes its connection, and one of the second's makes
        // its handshake in its place
        var failingConnection = await vpnServer1.TakeConnection(TestCt);
        failingConnection.Dispose();
        await AssertEqualsWait(1, () => vpnServer2.HelloCount,
            "No check that waits took the place of the one that ended.");

        Assert.AreEqual(1, vpnServer1.HelloCount, "The first server's other handshake ended.");
        Assert.IsFalse(connecting1.IsCompleted || connecting2.IsCompleted, "A connection's check ended.");

        await connectCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => connecting1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => connecting2);
    }

    // Each check beside the session had three handshakes of its own on iOS and tvOS, so two connections' own checks to
    // two servers made six at once. They share their places: here one, which goes from one to the other
    [TestMethod]
    public async Task The_connections_own_checks_share_their_handshake_places()
    {
        using var server = new PausingTlsServer().Start();
        using var vpnServer1 = new SilentProxy().Start();
        using var vpnServer2 = new SilentProxy().Start();
        using var proxy0 = new TestSocks5Proxy().Start();
        using var proxy1 = new TestSocks5Proxy().Start();
        using var proxy2 = new TestSocks5Proxy().Start();
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy0.EndPoint) },
            .. new[] { proxy1, proxy2 }.Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint, isEnabled: false),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            })
        ]);

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        connector.ConnectionHandshakePlaces = new SemaphoreSlim(1);

        // a proxy reaches a server, then goes off, and the connections check the two turned on in its place
        await connector.CheckServers(new TestSocketFactory(), server.EndPoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy0.EndPoint, isEnabled: false) },
            .. new[] { proxy1, proxy2 }.Select(x => new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(x.EndPoint) })
        ]);

        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        using var connectCts1 = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        using var connectCts2 = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var connecting1 = connector.ConnectAsync(new TestSocketFactory(), vpnServer1.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, connectCts1.Token);

        var connecting2 = connector.ConnectAsync(new TestSocketFactory(), vpnServer2.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, connectCts2.Token);

        // each of the four checks dialed its proxy, and the one with the place went on through its dial
        await AssertEqualsWait(4, () => proxy1.ConnectionCount + proxy2.ConnectionCount,
            "Not every check dialed its proxy.");

        await AssertEqualsWait(1, () => vpnServer1.HelloCount + vpnServer2.HelloCount, "No handshake began.");
        await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
        Assert.AreEqual(1, vpnServer1.HelloCount + vpnServer2.HelloCount,
            "The two connections' checks made a handshake each at once.");

        // the call that holds the place goes, and the other one's check takes it
        var isFirstHolding = vpnServer1.HelloCount == 1;
        await (isFirstHolding ? connectCts1 : connectCts2).CancelAsync();
        await AssertEqualsWait(1, () => (isFirstHolding ? vpnServer2 : vpnServer1).HelloCount,
            "The other connection's check did not take the place.");

        await connectCts1.CancelAsync();
        await connectCts2.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => connecting1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => connecting2);
    }

    // A check that waited for its turn at the handshakes had asked its proxy for the server already, and asked it again
    // after the wait though the user had turned the proxy off meanwhile. It asks nothing until its turn, and then only
    // of a proxy still on
    [TestMethod]
    public async Task A_check_waiting_for_its_turn_asks_nothing_of_a_proxy_turned_off_meanwhile()
    {
        using var vpnServer = new SilentProxy().Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var proxy1 = new TestSocks5Proxy().Start();
        using var proxy2 = new TestSocks5Proxy().Start();
        var socketFactory = new RecordingSocketFactory();
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var connection = await connector.ConnectAsync(socketFactory, vpnServer.EndPoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        using var appStore = await CreateStore(ToProxyEndPoint(proxy1.EndPoint), ToProxyEndPoint(proxy2.EndPoint));
        await connector.UpdateOptions(TlsCheckOptions, TestCt);

        // both checks dialed their proxy alone, and one has its turn
        await AssertEqualsWait(true, () => proxy1.ConnectionCount >= 1 && proxy2.ConnectionCount >= 1,
            "Not both checks dialed their proxy.");

        await AssertEqualsWait(1, () => vpnServer.HelloCount, "No handshake began.");
        var waiting = proxy1.Destinations.IsEmpty ? proxy1 : proxy2;
        Assert.AreEqual(1, new[] { proxy1, proxy2 }.Count(x => x.Destinations.IsEmpty),
            "A check that waits for its turn asked its proxy for the server.");

        // the other check closed its dial to wait for its turn, as one that dialed after the first check's end would
        // take the place free at once
        await AssertEqualsWait(1, () => waiting.BareCloseCount, "The other check does not wait for its turn.");

        // the user turns the waiting one off, and the first check's end gives it its turn, which makes it no socket,
        // as its making may take long and hold the place
        var socketCount = socketFactory.TcpClients.Count;
        await Upsert(appStore,
            [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(waiting.EndPoint, isEnabled: false) }]);

        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        vpnServer.CloseHelloConnections();
        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay, "The check did not end.");
        Assert.IsTrue(waiting.Destinations.IsEmpty, "A check asked a proxy turned off while it waited for its turn.");
        Assert.AreEqual(socketCount, socketFactory.TcpClients.Count,
            "A check made a socket for a proxy turned off while it waited for its turn.");
    }

    // On iOS and tvOS a connection's own check took its turn at the handshakes behind the checks of a run nobody waits
    // for, each of which may hold its place for the check's whole time: here 30 seconds. It has places of its own, and
    // that run goes on
    [TestMethod]
    public async Task A_connections_own_check_does_not_wait_behind_the_check_of_the_list()
    {
        // the server the list's check takes, which answers no hello, and the connection's, which holds the first hello
        // until the test lets it go
        using var silentServer = new SilentProxy().Start();
        using var vpnServer = new PausingTlsServer { HoldsFirst = true }.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listProxy = new TestSocks5Proxy().Start();
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var firstConnection = await connector.ConnectAsync(new TestSocketFactory(), silentServer.EndPoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        // the proxy the app adds gets a check behind the session, which holds the place
        using var appStore = await CreateStore(ToProxyEndPoint(listProxy.EndPoint));
        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        await AssertEqualsWait(1, () => silentServer.HelloCount, "The list's check did not take its place.");

        // the proxy that worked goes off, so the next connection checks for itself
        await Upsert(appStore,
            [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy.EndPoint, isEnabled: false) }]);

        await connector.UpdateOptions(TlsCheckOptions, TestCt);

        // the test holds the connections' only place: the own check dials the proxy alone, then waits for it, where the
        // list's check went on through its dial
        var connectionPlaces = connector.ConnectionHandshakePlaces ??
                               throw new InvalidOperationException("The connector has no places for connections.");

        await connectionPlaces.WaitAsync(TestCt);
        var connecting = connector.ConnectAsync(new TestSocketFactory(), vpnServer.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(2, () => listProxy.ConnectionCount, "The connection's own check did not dial alone.");
        await Task.Delay(TimeSpan.FromSeconds(0.5), TestCt);
        Assert.IsFalse(connecting.IsCompleted, "The connection's own check went around its place.");
        Assert.AreEqual(0, vpnServer.HelloTimes.Count,
            "The connection's own check made its handshake without its place.");

        // its place, and nothing else, lets it through, while the list's check holds the other place; it holds its own
        // through its handshake
        connectionPlaces.Release();
        await AssertEqualsWait(1, () => vpnServer.HelloTimes.Count, "The connection's own check made no handshake.");
        Assert.AreEqual(0, connectionPlaces.CurrentCount,
            "The connection's own check let its place go in its handshake.");

        vpnServer.ReleaseFirst();
        using var connection = await connecting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.IsTrue(connector.IsConnectCheckUnderWay, "The list's check did not go on.");
    }

    // A check that waited for its turn at the handshakes went on with the credentials its proxy had when the check
    // began, or was dropped where the user changed them meanwhile, so a corrected proxy kept the failure of its wrong
    // password: the run goes again over those never rated alone. It takes those the proxy has at its turn
    [TestMethod]
    public async Task A_check_that_waits_for_its_turn_takes_the_credentials_given_meanwhile()
    {
        using var vpnServer = new PausingTlsServer().Start();

        // a proxy that worked, and one whose old password failed
        using var proxy = new TestSocks5Proxy().Start();
        using var lockedProxy = new TestSocks5Proxy { Username = "user", Password = "new" }.Start();
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = WithPassword(lockedProxy.EndPoint, "old"),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            }
        ]);

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        // The test holds the only place. The check before the connect passes at the first proxy, before any server was
        // reached, and the second one's check, behind that pass, waits for the place with the old password
        var places = new SemaphoreSlim(1);
        connector.HandshakePlaces = places;
        connector.CheckWidth = 1;
        await places.WaitAsync(TestCt);
        await connector.CheckServers(new TestSocketFactory(), vpnServer.EndPoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(1, () => lockedProxy.ConnectionCount, "The check did not dial its proxy alone.");

        // the user corrects the password while the check waits, and its turn comes
        using var appStore = await CreateStore();
        await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = WithPassword(lockedProxy.EndPoint, "new") }]);
        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        places.Release();
        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay, "The check did not end.");
        var status = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == lockedProxy.EndPoint.Port).Status;
        Assert.IsTrue(status.IsLastUsedSucceeded, "The check did not take the password given while it waited.");
    }

    // A check that found its place at the handshakes free went on with the credentials its proxy had when the check
    // began, where one that waited for its place took those given meanwhile: a password corrected during its dial kept
    // the wrong one's failure. It takes those the proxy has once it has its place, either way
    [TestMethod]
    public async Task A_check_that_finds_a_place_free_takes_the_credentials_given_during_its_dial()
    {
        using var vpnServer = new PausingTlsServer().Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var lockedProxy = new TestSocks5Proxy { Username = "user", Password = "new" }.Start();
        var socketFactory = new HeldSocketFactory(TestCt);
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var connection = await connector.ConnectAsync(socketFactory, vpnServer.EndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        // the app adds the locked proxy with its old password, and its check behind the session is held in the making
        // of its socket, before its dial, while the user corrects the password; then it goes on, its place free
        socketFactory.IsHolding = true;
        using var appStore = await CreateStore(WithPassword(lockedProxy.EndPoint, "old"));
        try {
            await connector.UpdateOptions(TlsCheckOptions, TestCt);
            await socketFactory.Held.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
            await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = WithPassword(lockedProxy.EndPoint, "new") }]);
            await connector.UpdateOptions(TlsCheckOptions, TestCt);
        }
        finally {
            socketFactory.IsHolding = false;
            socketFactory.Release();
        }

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay, "The check did not end.");
        var status = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == lockedProxy.EndPoint.Port).Status;
        Assert.IsTrue(status.IsLastUsedSucceeded, "The check did not take the password given during its dial.");
    }

    // a SOCKS5 endpoint whose proxy asks for "user" and the password given
    private static ProxyEndPoint WithPassword(IPEndPoint endPoint, string password) => new() {
        Protocol = ProxyProtocol.Socks5,
        Host = endPoint.Address.ToString(),
        Port = endPoint.Port,
        Username = "user",
        Password = password
    };

    // A check that came before a proxy reached a server took no turn for the rest of its run, though the checks that
    // came after went beside the session: the server search's other probes went on 50 at a time. The gate reads it as
    // each check comes
    [TestMethod]
    public async Task The_handshake_gate_takes_turns_from_the_moment_a_server_is_reached()
    {
        using var places = new SemaphoreSlim(1);
        var isServerReached = false;
        var gate = new HandshakeGate(places, placesBeforeServer: null, isBesideSession: false, () => isServerReached);
        Assert.IsNull(await gate.Enter(TestCt), "A check took a turn before any server was reached.");

        isServerReached = true;
        var place = await gate.Enter(TestCt);
        Assert.AreSame(places, place, "A check that came once a server was reached took no turn.");

        var next = gate.Enter(TestCt);
        Assert.IsFalse(next.IsCompleted, "Two checks had a turn at once.");
        places.Release();
        Assert.AreSame(places, await next);
        places.Release();
    }

    // A check that waited for its place before a server, when a proxy reached one meanwhile, made its handshake with
    // the place it got there, beside the turns of the checks that came then: on iOS and tvOS beside their 3 places. It
    // takes its turn where they take theirs, and goes on at once where they take none
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task A_check_still_waiting_when_a_server_is_reached_takes_its_turn_where_the_checks_that_come_then_do(
        bool hasPlaces)
    {
        using var places = new SemaphoreSlim(1);
        using var placesBeforeServer = new SemaphoreSlim(1);
        var isServerReached = false;
        var gate = new HandshakeGate(hasPlaces ? places : null, placesBeforeServer, isBesideSession: false,
            () => isServerReached);

        // a check holds the only place before a server, and another waits for it
        Assert.AreSame(placesBeforeServer, await gate.Enter(TestCt));
        var waiting = gate.Enter(TestCt);

        // a proxy reaches a server, and a check that comes then holds the only place beside the session
        isServerReached = true;
        if (hasPlaces)
            Assert.AreSame(places, await gate.Enter(TestCt));

        // the first check's end leaves the waiting one no place before a server
        placesBeforeServer.Release();
        await AssertEqualsWait(1, () => placesBeforeServer.CurrentCount,
            "The waiting check kept its place before a server.");

        if (!hasPlaces) {
            Assert.IsNull(await waiting, "The waiting check took a place where those that come take none.");
            return;
        }

        Assert.IsFalse(waiting.IsCompleted, "The waiting check went on beside the turns of the checks that came.");
        places.Release();
        Assert.AreSame(places, await waiting);
        places.Release();
    }

    // Before its first pass the check before a connect makes its TLS handshakes as many at once as it dials, as the
    // connect waits for it; once one passed, it goes beside the session, and those that come take their turns
    [TestMethod]
    public async Task The_check_before_a_connect_takes_turns_at_the_handshakes_once_one_passed()
    {
        // a VPN server that answers the first two hellos once both came, and none after them
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var vpnServer = new TcpListener(IPAddress.Loopback, 0);
        vpnServer.Start();
        var helloCount = 0;
        var bothCame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Serve();

        using var proxy1 = new TestSocks5Proxy().Start();
        using var proxy2 = new TestSocks5Proxy().Start();
        using var proxy3 = new TestSocks5Proxy().Start();
        using var proxy4 = new TestSocks5Proxy().Start();
        var store = await CreateStore();
        await Upsert(store, [
            .. new[] { proxy1, proxy2 }.Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }),
            .. new[] { proxy3, proxy4 }.Select(x => new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(x.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            })
        ]);

        await using var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        connector.CheckWidth = 2;
        connector.HandshakePlaces = new SemaphoreSlim(1);
        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)vpnServer.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        await AssertEqualsWait(3, () => Volatile.Read(ref helloCount), "No check behind the pass began its handshake.");
        await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
        Assert.AreEqual(3, Volatile.Read(ref helloCount), "Behind its pass, the check made two handshakes at once.");
        return;

        async Task Serve()
        {
            while (true) {
                var client = await vpnServer.AcceptTcpClientAsync(TestCt);
                _ = Answer(client);
            }
        }

        async Task Answer(TcpClient client)
        {
            try {
                if (await client.Client.ReceiveAsync(new byte[1], SocketFlags.Peek, TestCt) == 0)
                    return;

                var number = Interlocked.Increment(ref helloCount);
                if (number > 2) {
                    // unanswered, until the check goes
                    while (await client.GetStream().ReadAsync(new byte[1024], TestCt) > 0) { }

                    return;
                }

                if (number == 2)
                    bothCame.TrySetResult();

                await bothCame.Task.WaitAsync(TestCt);
                await using var sslStream = new SslStream(client.GetStream());
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);
            }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException
                                           or OperationCanceledException) {
                // a check that went
            }
            finally {
                client.Dispose();
            }
        }
    }
}
