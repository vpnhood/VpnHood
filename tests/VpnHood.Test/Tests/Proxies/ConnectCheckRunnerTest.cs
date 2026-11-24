using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ConnectCheckRunnerTest : ProxyTestBase
{
    // A reset that changed no endpoint left every one unrated, and the check of what a reload brings took them all: a
    // check of the whole list nobody asked for, beside the session, whose failures a reset's test then counted
    [TestMethod]
    public async Task A_reset_starts_no_check_of_the_endpoints_it_left_as_they_were()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var proxyOptions = new ProxyOptions { VerifyTls = false };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        // a pass: the server the checks behind the session take
        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        var dialCount = proxy.Destinations.Count;
        await connector.UpdateOptions(new ProxyOptions { VerifyTls = false, ResetStates = true }, TestCt);

        // to the end of any check the reset started
        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay, "A check after the reset did not end.");
        await AssertEqualsWait(true, () => connector.Progress is null, "A check after the reset did not end.");
        Assert.AreEqual(dialCount, proxy.Destinations.Count, "A check after the reset dialed an endpoint it left on.");
    }

    [TestMethod]
    public async Task The_check_before_a_connect_ends_its_wait_at_its_first_pass_and_the_close_stops_the_rest()
    {
        // The connect waited for every proxy's check, then for the first pass with the rest stopped and unrated. It
        // now goes on at the first pass, here the answering proxy's, while the silent proxy's check goes on; the close
        // stops that one, waits for it, and saves what the check found, with no failure for the check it stopped and
        // every socket closed
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var silentCheckStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy(standIn: (IPEndPoint)target.LocalEndpoint) {
            ReplyGate = silentCheckStarted.Task
        }.Start();

        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var silentEndPoint = (IPEndPoint)silentProxy.LocalEndpoint;

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint(silentEndPoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var socketFactory = new RecordingSocketFactory();
        var checkTask = connector.CheckServers(socketFactory, (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, TestCt);

        using var silentAttempt = await silentProxy.AcceptTcpClientAsync(TestCt);
        silentCheckStarted.TrySetResult();
        await checkTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        var proxyId = ToProxyEndPoint(proxy.EndPoint).Id;
        Assert.AreEqual(1, connector.GetEndPointInfos().Single(x => x.EndPoint.Id == proxyId).Status.SucceededCount);
        Assert.HasCount(2, socketFactory.TcpClients);
        Assert.IsNotNull(connector.Progress, "The silent proxy's check should still be under way.");

        await connector.DisposeAsync();

        Assert.AreEqual(0, connector.GetEndPointInfos().Single(x => x.EndPoint.Port == silentEndPoint.Port)
            .Status.FailedCount, "A check the close stopped is no failure.");

        foreach (var tcpClient in socketFactory.TcpClients)
            Assert.ThrowsExactly<ObjectDisposedException>(() => tcpClient.GetStream());

        using var savedStore = await CreateStore();
        var saved = await savedStore.Get(proxyId, TestCt);
        Assert.IsNotNull(saved);
        Assert.AreEqual(1, saved.Status.SucceededCount);
    }

    // The connect's check ended at its first pass, so a proxy whose check that pass stopped stayed unrated, and the
    // connections took the one that passed alone. The rest is now checked behind the connect, and the connections take
    // every proxy that passed, each in its turn
    [TestMethod]
    public async Task The_check_before_a_connect_rates_the_rest_behind_it_for_the_connections_that_follow()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var fastProxy = new TestSocks5Proxy().Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(fastProxy.EndPoint), ToProxyEndPoint(heldProxy.EndPoint)),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        await connector.CheckServers(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        var heldStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == heldProxy.EndPoint.Port).Status;
        Assert.IsFalse(heldStatus.HasUsed, "The connect should go on before the held proxy answers.");
        await AssertEqualsWait(1, () => heldProxy.Destinations.Count, "The held proxy's check did not start.");

        release.TrySetResult();
        await AssertEqualsWait(true, () => heldStatus.IsLastUsedSucceeded, "The held proxy's check did not go on.");

        for (var i = 0; i < 6; i++) {
            using var connection = await connector.ConnectAsync(new TestSocketFactory(), targetEndPoint,
                hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);
        }

        // each taken, whatever penalty the held proxy's wait earned it, and each connection dialed once
        Assert.IsGreaterThan(1, heldProxy.Destinations.Count, "The connections should take the held proxy too.");
        Assert.IsGreaterThan(1, fastProxy.Destinations.Count);
        Assert.AreEqual(8, heldProxy.Destinations.Count + fastProxy.Destinations.Count);
    }

    // A check the next one replaced went on through the list beside it, against a server that may be no longer the
    // connect's: the next one stops it
    [TestMethod]
    public async Task The_next_check_before_a_connect_stops_the_last_ones_rest()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        // the first check's socket, open until the next check
        using var firstSilentAttempt = await silentProxy.AcceptTcpClientAsync(TestCt);
        var firstSilentClose = ReadUntilPeerClose(firstSilentAttempt);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestCt);
        Assert.IsFalse(firstSilentClose.IsCompleted, "The first check's silent proxy should still be under check.");

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await firstSilentClose.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
    }

    // A call whose token was canceled already put its own check in place of the one under way, which it stopped, and
    // then stopped its own: the rest went unrated. It now starts nothing, and stops nothing
    [TestMethod]
    public async Task A_check_canceled_before_its_call_stops_no_check_under_way()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();

        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint(heldProxy.EndPoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(1, () => heldProxy.Destinations.Count, "The held proxy's check did not start.");
        var socketFactory = new RecordingSocketFactory();
        await Assert.ThrowsAsync<OperationCanceledException>(() => connector.CheckServers(socketFactory,
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null,
            new CancellationToken(canceled: true)));

        Assert.IsEmpty(socketFactory.TcpClients, "The canceled call dialed.");

        // the check under way goes on to its end, and records what it finds
        release.TrySetResult();
        var heldStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == heldProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => heldStatus.IsLastUsedSucceeded,
            "The canceled call stopped the check under way.");
    }

    // what the peer sends until its close, which ends the task; a reset is a close too
    private async Task ReadUntilPeerClose(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[256];
        try {
            while (await stream.ReadAsync(buffer, TestCt) > 0) {
                // what the peer sent before its close
            }
        }
        catch (IOException) {
            // a reset
        }
    }

    // The close left a check before a connect running, on the sockets of a client that was closing, until the check's
    // time ran out. It now stops the check, which records nothing it stopped, and the connect's wait ends with the close
    [TestMethod]
    public async Task The_close_ends_a_check_before_a_connect_that_has_no_pass_yet()
    {
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint)), socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var checkTask = connector.CheckServers(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, TestCt);

        using var silentAttempt = await silentProxy.AcceptTcpClientAsync(TestCt);
        await connector.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            checkTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));

        Assert.AreEqual(0, connector.GetEndPointInfos().Single().Status.FailedCount);
    }

    // The close left a connection's own check running, on the sockets of a client that was closing, past its save. It
    // now stops every check, which records nothing it stopped, and waits for them before its save
    [TestMethod]
    public async Task The_close_stops_a_connections_own_check()
    {
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint)), socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        using var silentAttempt = await silentProxy.AcceptTcpClientAsync(TestCt);
        var silentClose = ReadUntilPeerClose(silentAttempt);
        await connector.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);

        await silentClose.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            connecting.WaitAsync(TimeSpan.FromSeconds(5), TestCt));

        Assert.AreEqual(0, connector.GetEndPointInfos().Single().Status.FailedCount);
    }

    // A proxy the user turned off while its check ran still counted as the check's pass, and let the connect go on with
    // no proxy it could take. Its pass is now recorded alone
    [TestMethod]
    public async Task A_proxy_turned_off_during_its_check_lets_no_connect_go_on()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        var proxyOptions = new ProxyOptions { VerifyTls = false };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(heldProxy.EndPoint),
                ToProxyEndPoint((IPEndPoint)silentProxy.LocalEndpoint)),
            socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var checkTask = connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, TestCt);

        // the user turns the held proxy off while its check waits for its answer
        await AssertEqualsWait(1, () => heldProxy.Destinations.Count, "The held proxy's check did not start.");
        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(heldProxy.EndPoint, isEnabled: false) }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        release.TrySetResult();

        var heldStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == heldProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => heldStatus.IsLastUsedSucceeded, "The held proxy's pass was not recorded.");
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestCt);
        Assert.IsFalse(checkTask.IsCompleted, "The connect went on at the pass of a proxy turned off.");
    }

    // After every proxy failed, as on a change of network, a connection checked until one passed, and the session then
    // ran on that one alone: nothing checked the others again until the next connect. The rest is now checked behind
    // that pass
    [TestMethod]
    public async Task A_connections_own_check_rates_the_rest_behind_its_pass()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var firstProxy = new TestSocks5Proxy().Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();

        // both failed last, as after a change of network
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(firstProxy.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(heldProxy.EndPoint),
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            }
        ]);

        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.AreEqual(firstProxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        release.TrySetResult();
        var heldStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == heldProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => heldStatus.IsLastUsedSucceeded, "The rest was not checked behind the pass.");
    }

    // A list's first download during the check before a connect brought endpoints neither check took: the run had
    // taken its list, and a job's first download checks nothing. The run now goes on over those never rated
    [TestMethod]
    public async Task The_check_before_a_connect_rates_the_endpoints_a_reload_brought_during_it()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heldProxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        using var newProxy = new TestSocks5Proxy().Start();
        var proxyOptions = new ProxyOptions { VerifyTls = false };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint), ToProxyEndPoint(heldProxy.EndPoint)),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        // the app adds an endpoint while the held proxy's check keeps the run going
        using var appStore = await CreateStore(ToProxyEndPoint(newProxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);
        release.TrySetResult();

        var newStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == newProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => newStatus.IsLastUsedSucceeded,
            "The endpoint the reload brought was not checked.");
    }

    // A list's first download that came once the check before the connect had ended brought endpoints no check took:
    // the job's first download checks nothing, and the run was over. Those never rated now get a check of their own
    [TestMethod]
    public async Task A_first_download_after_the_check_before_the_connect_ended_has_its_endpoints_checked()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://127.0.0.1:{newProxy.EndPoint.Port}", isHeld: true, TestCt);

        // both on this machine's address, which the rule of one endpoint per address would leave to the stored one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions {
                Url = listServer.Url, Interval = TimeSpan.FromHours(1), RemoveDuplicateIps = false
            }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        listServer.Release();
        await AssertEqualsWait(true, () => connector.GetEndPointInfos()
                .Any(x => x.EndPoint.Port == newProxy.EndPoint.Port && x.Status.IsLastUsedSucceeded),
            "The endpoint the download brought was not checked.");
    }

    // An endpoint the app added once the check before the connect had ended stayed unrated beside a healthy proxy,
    // which carried the connections: nothing checked it. It is now checked behind the session
    [TestMethod]
    public async Task An_endpoint_the_app_adds_after_the_check_before_the_connect_ended_is_checked()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        using var appStore = await CreateStore(ToProxyEndPoint(newProxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);

        var newStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == newProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => newStatus.IsLastUsedSucceeded, "The endpoint the app added was not checked.");
    }

    // The checks behind the session took the server of the last call, which a server search's probe of a server that is
    // down could be: every proxy they checked was rated failed. They take the last server a proxy reached now
    [TestMethod]
    public async Task The_checks_behind_the_session_take_the_last_server_a_proxy_reached()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        // a server that is down: a port that takes no connection, held so no listener of the test takes it
        using var downSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        downSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var downEndPoint = downSocket.LocalEndPoint as IPEndPoint ??
                           throw new InvalidOperationException("The bound socket has no endpoint.");

        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        // a probe of a server that is down, through the proxy that passed
        await Assert.ThrowsExactlyAsync<SocketException>(() => connector.ConnectAsync(new TestSocketFactory(),
            downEndPoint, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt));

        // the app adds an endpoint, which a check behind the session takes
        using var appStore = await CreateStore(ToProxyEndPoint(newProxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);

        var newStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == newProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => newStatus.HasUsed, "The endpoint the app added was not checked.");
        Assert.IsTrue(newStatus.IsLastUsedSucceeded, "The check took the probe's server, which is down.");
    }

    // A session whose connections never checked, as with a key known by name alone and a proxy that worked last, had no
    // server for the checks behind it: an endpoint the app added stayed unrated. Where no check passed, the server of a
    // connection that reached it serves
    [TestMethod]
    public async Task With_no_check_passed_the_checks_behind_the_session_take_a_connections_server()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        // a connection through the proxy that worked last, with no check
        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        using var appStore = await CreateStore(ToProxyEndPoint(newProxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);

        var newStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == newProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => newStatus.IsLastUsedSucceeded, "The endpoint the app added was not checked.");
    }

    // A connection that waited for a list's download checked its proxy, and so did a check the download started behind
    // the session, beside it: the proxy was dialed a third time for the connection. Where a connection waits for the
    // list, it checks what the list brings alone
    [TestMethod]
    public async Task A_download_a_connection_waits_for_starts_no_check_beside_it()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;

        // the connection's server, apart from the check's, so a check behind the session shows in what the proxy was asked
        using var connectionTarget = new TcpListener(IPAddress.Loopback, 0);
        connectionTarget.Start();
        var connectionTargetEndPoint = (IPEndPoint)connectionTarget.LocalEndpoint;
        using var storedProxy = new TestSocks5Proxy().Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://127.0.0.1:{proxy.EndPoint.Port}", isHeld: true, TestCt);

        // a list with no interval downloads nothing while an endpoint is enabled; both on this machine's address
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { Url = listServer.Url, RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(storedProxy.EndPoint)), socketFactory: null, serverCheckTimeout: null,
            TestCt);

        // a pass: the server the checks behind the session take
        await connector.CheckServers(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        // the user turns the endpoint off: the list downloads again, and a connection waits for it
        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(storedProxy.EndPoint, isEnabled: false) }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list was not downloaded again.");
        var connecting = connector.ConnectAsync(new TestSocketFactory(), connectionTargetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => connector.EndPointWaiterCount, "The connection did not wait for the list.");
        listServer.Release();
        using var connection = await connecting;

        // the connection's own check and its dial, both to its server, once every check ended
        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay, "A check behind the session did not end.");
        await AssertEqualsWait(true, () => connector.Progress is null, "A check did not end.");
        Assert.AreEqual(2, proxy.Destinations.Count, "The proxy was not asked twice.");
        Assert.IsTrue(proxy.Destinations.All(x => x.Equals(connectionTargetEndPoint)),
            "A check beside the connection dialed its proxy.");
    }

    // With every endpoint off, the app turned one back on and added another: the check of what a reload brings took
    // nothing, as the list had none on before, though no connection waited for it, and the one added stayed unrated
    // beside the one that worked. Only a connection that waits for the list leaves them to its own check
    [TestMethod]
    public async Task Endpoints_the_app_turns_on_and_adds_with_the_rest_off_are_checked()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();

        // both on this machine's address, which the rule of one endpoint per address would leave to one
        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(false, () => connector.IsConnectCheckUnderWay,
            "The check before the connect did not end.");

        // every endpoint off, then the one back on and another added
        using var appStore = await CreateStore();
        await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy.EndPoint, isEnabled: false) }]);
        await connector.UpdateOptions(proxyOptions, TestCt);
        await Upsert(appStore, [
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy.EndPoint) },
            new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(newProxy.EndPoint) }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        var newStatus = connector.GetEndPointInfos().Single(x => x.EndPoint.Port == newProxy.EndPoint.Port).Status;
        await AssertEqualsWait(true, () => newStatus.IsLastUsedSucceeded, "The endpoint the app added was not checked.");
    }

    // The check before a connect began with the endpoints never used, as Unknown comes first in the quality's order:
    // with 50 or more of them silent ahead of a proxy known to work, the connect waited out a silent one's whole time.
    // Those that succeeded last now come first
    [TestMethod]
    public async Task The_check_before_a_connect_tries_a_proxy_that_succeeded_last_before_those_never_used()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var silentProxies = Enumerable.Range(0, 60).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        try {
            foreach (var silentProxy in silentProxies)
                silentProxy.Start();

            var store = await CreateStore(silentProxies
                .Select(x => ToProxyEndPoint((IPEndPoint)x.LocalEndpoint))
                .ToArray());

            await Upsert(store, [
                new ProxyEndPointRecord {
                    EndPoint = ToProxyEndPoint(proxy.EndPoint),
                    Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
                }
            ]);

            await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
                store, socketFactory: null, serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

            await connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
                serverCertificateValidation: null, TestCt).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

            Assert.AreEqual(1, proxy.Destinations.Count, "The check went on at the stored success, with no check of it.");
        }
        finally {
            foreach (var silentProxy in silentProxies)
                silentProxy.Dispose();
        }
    }

    // After every proxy failed, as on a change of network, a connection checked until one passed, and the check of the
    // rest behind it took the proxy that passed first among them, beside the connection's own dial. It now takes what
    // the connection's check left, as many at once as the check's width: here two
    [TestMethod]
    public async Task The_check_behind_a_connections_own_check_takes_what_it_left()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var silentProxies = Enumerable.Range(0, 3).Select(_ => new SilentProxy().Start()).ToArray();
        try {
            // all failed last, the answering one first in turn
            var store = await CreateStore();
            await Upsert(store, [
                new ProxyEndPointRecord {
                    EndPoint = ToProxyEndPoint(proxy.EndPoint),
                    Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow.AddMinutes(-1) }
                },
                .. silentProxies.Select(x => new ProxyEndPointRecord {
                    EndPoint = ToProxyEndPoint(x.EndPoint),
                    Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
                })
            ]);

            var proxyOptions = new ProxyOptions {
                VerifyTls = false,
                AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
            };

            await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
                serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

            connector.CheckWidth = 2;
            var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
                hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

            // the connection's check dials the answering proxy and a silent one at once, and passes at the first
            await AssertEqualsWait(1, () => silentProxies.Sum(x => x.ConnectionCount),
                "The connection's check dialed no silent proxy beside the answering one.");

            release.TrySetResult();
            using var connection = await connecting;

            // the check behind the connection dials two of the silent ones, the one the connection's check left too
            await AssertEqualsWait(3, () => silentProxies.Sum(x => x.ConnectionCount),
                "The rest was not checked behind the connection's pass.");

            await Task.Delay(TimeSpan.FromSeconds(1), TestCt);
            Assert.AreEqual(3, silentProxies.Sum(x => x.ConnectionCount),
                "The check behind the connection dialed more at once than its width.");

            Assert.AreEqual(2, proxy.Destinations.Count,
                "The check behind the connection dialed the proxy that passed, beside the connection.");
        }
        finally {
            foreach (var silentProxy in silentProxies)
                silentProxy.Dispose();
        }
    }

    // A connection's success gave the checks behind the session its server where no check had passed, and kept it there
    // when the VPN's TLS then failed on it, as with a server that is no VPN server, or did not end in time, which reads as
    // a cancellation: the checks rated every proxy against it. That connection's failure takes it back
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_connections_failure_takes_back_the_server_it_gave_the_checks(bool isTimeout)
    {
        Exception failure = isTimeout
            ? new OperationCanceledException("The VPN's TLS did not end in time.")
            : new AuthenticationException("The server's certificate was refused.");

        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var newProxy = new TestSocks5Proxy().Start();
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        connector.RecordFailed(connection.TcpClient, failure);
        using var appStore = await CreateStore(ToProxyEndPoint(newProxy.EndPoint));
        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        Assert.IsFalse(connector.IsConnectCheckUnderWay, "A check took the server of a connection that failed.");
    }

    // A connection's own check that had to take turns stopped the check of the list first, and where it found no proxy,
    // what the list's check had left stayed unrated. It has places of its own, and the list's check goes on
    [TestMethod]
    public async Task A_connections_own_check_leaves_the_check_of_the_list_running()
    {
        // the server the list's check takes, which answers no hello, and one that is down: a port that takes no
        // connection, held so no listener takes it
        using var silentServer = new SilentProxy().Start();
        using var downSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        downSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var unreachableEndPoint = downSocket.LocalEndPoint as IPEndPoint ??
                                  throw new InvalidOperationException("The bound socket has no endpoint.");

        using var proxy = new TestSocks5Proxy().Start();
        using var listProxy = new TestSocks5Proxy().Start();
        await using var connector = await CreateConnectorWithAWorkingProxy(proxy, TimeSpan.FromSeconds(30));
        using var firstConnection = await connector.ConnectAsync(new TestSocketFactory(), silentServer.EndPoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        using var appStore = await CreateStore(ToProxyEndPoint(listProxy.EndPoint));
        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        await AssertEqualsWait(1, () => silentServer.HelloCount, "The list's check did not take its place.");

        // the proxy that worked goes off, and the next connection, whose server is down, checks for itself and fails
        await Upsert(appStore,
            [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(proxy.EndPoint, isEnabled: false) }]);

        await connector.UpdateOptions(TlsCheckOptions, TestCt);
        await Assert.ThrowsAsync<SocketException>(() => connector.ConnectAsync(new TestSocketFactory(),
            unreachableEndPoint, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt));

        Assert.IsTrue(connector.IsConnectCheckUnderWay, "The connection's own check stopped the check of the list.");
    }
}
