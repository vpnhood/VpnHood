using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;
using VpnHood.Test.Providers.Proxies;

namespace VpnHood.Test.Tests.Proxies;

[TestClass]
public class ProxyEndPointListTest : ProxyTestBase
{
    // A connector whose one proxy has a status its store has not saved yet: a connect through it succeeded. The proxy
    // succeeded last, so the connect checks nothing, and no check behind it saves the status first
    private async Task<ConnectorWithStore> CreateConnectorWithUnsavedStatus()
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var innerStore = await CreateStore();
        await Upsert(innerStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(socks5ProxyServer.ListenerEndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var store = new TestProxyEndPointStore(innerStore);
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null,
            TestCt);

        return new ConnectorWithStore(connector, store);
    }

    [TestMethod]
    public async Task A_second_close_waits_for_the_first_ones_flush()
    {
        var unsaved = await CreateConnectorWithUnsavedStatus();

        // the first close's flush is held
        unsaved.Store.HoldUpdates = true;
        var firstClose = unsaved.Connector.DisposeAsync().AsTask();
        await VhTestUtil.AssertEqualsWait(1, () => Volatile.Read(ref unsaved.Store.HeldUpdateCount),
            "The first close should flush.", cancellationToken: TestCt);

        var secondClose = unsaved.Connector.DisposeAsync().AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestCt);
        Assert.IsFalse(secondClose.IsCompleted);

        unsaved.Store.ReleaseUpdates();
        await Task.WhenAll(firstClose, secondClose).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsTrue(unsaved.Store.IsDisposed);
    }

    [TestMethod]
    public async Task A_failed_flush_still_closes_the_store()
    {
        var unsaved = await CreateConnectorWithUnsavedStatus();

        unsaved.Store.FailUpdates = true;
        await unsaved.Connector.DisposeAsync();
        Assert.IsTrue(unsaved.Store.IsDisposed);
    }

    // A reset of the statuses after the connect: the connection's failure took its success back from the reset count,
    // which went below zero
    [TestMethod]
    public async Task A_failure_after_a_reset_of_the_statuses_takes_no_count_below_zero()
    {
        var connection = await ConnectThroughProxy();
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;

        await connector.UpdateOptions(new ProxyOptions { ResetStates = true }, TestCt);
        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));

        var status = connector.GetEndPointInfos().Single().Status;
        Assert.AreEqual(0, status.SucceededCount);
        Assert.AreEqual(1, status.FailedCount);
    }

    // A reset of the statuses that the store failed to make dropped the statuses not saved yet all the same, as the
    // reload read the store, and counted as made, so a connection's failure after it took no success back. A failed
    // reset now changes nothing: the statuses are saved, and the success is taken back
    [TestMethod]
    public async Task A_reset_the_store_failed_changes_nothing()
    {
        TestProxyEndPointStore? store = null;
        var connection = await ConnectThroughProxy(wrapStore: x => store = new TestProxyEndPointStore(x));
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        Assert.IsNotNull(store);
        var succeededCount = connector.GetEndPointInfos().Single().Status.SucceededCount;

        store.FailResets = true;
        await connector.UpdateOptions(new ProxyOptions { ResetStates = true }, TestCt);
        var status = connector.GetEndPointInfos().Single().Status;
        Assert.AreEqual(succeededCount, status.SucceededCount, "The failed reset dropped the connection's success.");

        connector.RecordFailed(tcpClient, new AuthenticationException("The TLS handshake failed."));
        Assert.AreEqual(succeededCount - 1, status.SucceededCount);
        Assert.AreEqual(1, status.FailedCount);
    }

    // A connection's failure after a reset of the statuses took its success back from the count of the connections
    // made since, which the reset had dropped already
    [TestMethod]
    public async Task A_failure_after_a_reset_takes_back_no_later_connections_success()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var proxyOptions = new ProxyOptions { VerifyTls = false };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await connector.UpdateOptions(new ProxyOptions { VerifyTls = false, ResetStates = true }, TestCt);
        using var laterConnection = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null,
            TestCt);

        // the later connection checked, and a pass of the check behind it would land between the read and the assert
        await AssertEqualsWait(true, () => connector.Progress is null, "The check behind the connection did not end.");
        var status = connector.GetEndPointInfos().Single().Status;
        var succeededCount = status.SucceededCount;
        connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));

        Assert.AreEqual(succeededCount, status.SucceededCount);
        Assert.AreEqual(1, status.FailedCount);
    }

    // A connection whose dial ended while a reset's reload was under way read the reset count before the reload's end,
    // which counted the reset, and put its success on an entry the reload made: its failure after then took nothing
    // back, and the proxy kept a success it never had. The count now comes from the entry that takes the success
    [TestMethod]
    public async Task A_failure_takes_back_the_success_its_connection_put_on_an_entry_a_reset_made()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var innerStore = await CreateStore();
        await Upsert(innerStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var store = new TestProxyEndPointStore(innerStore);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The connection did not reach the proxy.");

        // the reset's reload holds after it made the entries anew, while the connection's dial ends
        store.HoldQueuePositionReads = true;
        var updating = connector.UpdateOptions(new ProxyOptions { VerifyTls = false, ResetStates = true }, TestCt);
        try {
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldQueuePositionReadCount),
                "The reset's reload did not start.");

            release.TrySetResult();
            using var connection = await connecting;
            store.ReleaseQueuePositionReads();
            await updating;

            var status = connector.GetEndPointInfos().Single().Status;
            Assert.AreEqual(1, status.SucceededCount, "The connection's success is not on the entry the reset made.");
            connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));
            Assert.AreEqual(0, status.SucceededCount, "The failure left its connection's success in place.");
            Assert.AreEqual(1, status.FailedCount);
        }
        finally {
            store.ReleaseQueuePositionReads();
            release.TrySetResult();
        }
    }

    // A connection whose dial ended while a reset's reload read the store put its success on an entry the reload then
    // replaced with a clean one: its failure after took back the success of a later connection, on that new entry
    [TestMethod]
    public async Task A_failure_after_a_reset_takes_back_no_success_a_later_connection_put_on_the_new_entry()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var innerStore = await CreateStore();
        await Upsert(innerStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var store = new TestProxyEndPointStore(innerStore);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        var connecting = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The connection did not reach the proxy.");

        // the reset's reload holds at its read of the store, while the connection's dial ends
        store.HoldLists = true;
        var updating = connector.UpdateOptions(new ProxyOptions { VerifyTls = false, ResetStates = true }, TestCt);
        try {
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldListCount),
                "The reset's reload did not start.");

            release.TrySetResult();
            using var connection = await connecting;
            store.HoldLists = false;
            store.ReleaseLists();
            await updating;

            // a later connection's success, on the entry the reset made
            using var laterConnection = await connector.ConnectAsync(new TestSocketFactory(), targetEndPoint,
                hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

            await AssertEqualsWait(true, () => connector.Progress is null, "The later connection's check did not end.");
            var status = connector.GetEndPointInfos().Single().Status;
            var succeededCount = status.SucceededCount;
            var failedCount = status.FailedCount;
            Assert.IsGreaterThan(0, succeededCount, "The later connection's success is not on the new entry.");

            connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));
            Assert.AreEqual(succeededCount, status.SucceededCount,
                "The failure took back the later connection's success.");

            Assert.AreEqual(failedCount + 1, status.FailedCount, "The failure was not recorded.");
        }
        finally {
            store.ReleaseLists();
            release.TrySetResult();
        }
    }

    // A reset of the statuses dropped what was not saved yet, an endpoint a duplicate's success had turned off among
    // it, which the store's older state then turned back on. What is not saved goes to the store before the reset now,
    // and the reset keeps the enabled states
    [TestMethod]
    public async Task A_reset_keeps_a_duplicate_turned_off_before_it_was_saved()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var duplicateProxy = new TestSocks5Proxy().Start();

        // both succeeded last, the first first in turn; both on this machine's address, so a success turns the other
        // off
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(duplicateProxy.EndPoint),
                Status = new ProxyEndPointStatus {
                    SucceededCount = 1, LastSucceeded = DateTime.UtcNow, QueuePosition = 100
                }
            }
        ]);

        var proxyOptions = new ProxyOptions {
            VerifyTls = false,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = true }
        };

        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        var duplicateId = ToProxyEndPoint(duplicateProxy.EndPoint).Id;
        Assert.IsFalse(connector.GetEndPointInfos().Single(x => x.EndPoint.Id == duplicateId).EndPoint.IsEnabled,
            "The connection's success did not turn its duplicate off.");

        // not saved yet, as the reset comes
        using (var storeBefore = await CreateStore())
            Assert.IsTrue((await storeBefore.Get(duplicateId, TestCt))?.EndPoint.IsEnabled,
                "The duplicate's turning off was saved before the reset.");

        await connector.UpdateOptions(new ProxyOptions {
            VerifyTls = false,
            ResetStates = true,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = true }
        }, TestCt);

        Assert.IsFalse(connector.GetEndPointInfos().Single(x => x.EndPoint.Id == duplicateId).EndPoint.IsEnabled,
            "The reset turned the duplicate back on.");

        using var savedStore = await CreateStore();
        var saved = await savedStore.Get(duplicateId, TestCt);
        Assert.IsNotNull(saved);
        Assert.IsFalse(saved.EndPoint.IsEnabled, "The store has the duplicate on.");
    }

    // The app resets the shared store before it asks the connector to: the connector first wrote the counts it held
    // and had not saved, and a close or a failure before its own reset left them over the app's. Its own reset comes
    // first now, and no count held is written back
    [TestMethod]
    public async Task A_reset_writes_no_count_it_held_over_the_apps_reset()
    {
        TestProxyEndPointStore? store = null;
        var connection = await ConnectThroughProxy(wrapStore: x => store = new TestProxyEndPointStore(x));
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        Assert.IsNotNull(store);

        // the app's reset of the shared store, while the connector holds the connection's success unsaved
        using (var appStore = await CreateStore())
            await appStore.ResetStatuses(TestCt);

        store.HoldResets = true;
        var updating = connector.UpdateOptions(new ProxyOptions { ResetStates = true }, TestCt);
        try {
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldResetCount),
                "The connector's reset did not start.");

            using var savedStore = await CreateStore();
            var saved = (await savedStore.List(TestCt)).Single();
            Assert.AreEqual(0, saved.Status.SucceededCount, "The connector wrote its counts over the app's reset.");
        }
        finally {
            store.ReleaseResets();
        }

        await updating;
    }

    // A reset whose reload had made the entries anew, and failed at its last read, put fresh ones in their place: a
    // connection's success on one, with the reset's count, went with them, and its failure after would take back a
    // later connection's. The entries the reload made stay now
    [TestMethod]
    public async Task A_reset_whose_reload_fails_after_it_made_the_entries_keeps_them()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proxy = new TestSocks5Proxy { ReplyGate = release.Task }.Start();
        var innerStore = await CreateStore();
        await Upsert(innerStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var store = new TestProxyEndPointStore(innerStore);
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The connection did not reach the proxy.");

        // the reset's reload holds at its last read, after it made the entries, and fails there
        store.HoldQueuePositionReads = true;
        using var updateCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var updating = connector.UpdateOptions(new ProxyOptions { VerifyTls = false, ResetStates = true },
            updateCts.Token);

        try {
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldQueuePositionReadCount),
                "The reset's reload did not start.");

            release.TrySetResult();
            using var connection = await connecting;
            await updateCts.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => updating);

            var status = connector.GetEndPointInfos().Single().Status;
            Assert.AreEqual(1, status.SucceededCount,
                "The entry the reload made, with the connection's success, went.");

            connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));
            Assert.AreEqual(0, status.SucceededCount);
        }
        finally {
            store.ReleaseQueuePositionReads();
            release.TrySetResult();
        }
    }

    // A reset the store made, whose reload after it failed, left the old statuses held, and the next save wrote those
    // that had changed back over the reset. The held ones are now cleared with the store's
    [TestMethod]
    public async Task A_reset_whose_reload_fails_writes_no_old_status_back()
    {
        TestProxyEndPointStore? store = null;
        var connection = await ConnectThroughProxy(wrapStore: x => store = new TestProxyEndPointStore(x));
        await using var connector = connection.Connector;
        using var tcpClient = connection.TcpClient;
        Assert.IsNotNull(store);

        store.FailLists = true;
        await Assert.ThrowsAsync<IOException>(() =>
            connector.UpdateOptions(new ProxyOptions { ResetStates = true }, TestCt));

        Assert.AreEqual(0, connector.GetEndPointInfos().Single().Status.SucceededCount,
            "The held status was not cleared with the store's.");

        store.FailLists = false;
        await connector.Flush(TestCt);
        using var savedStore = await CreateStore();
        var saved = (await savedStore.List(TestCt)).Single();
        Assert.AreEqual(0, saved.Status.SucceededCount, "The save wrote an old status back over the reset.");
    }

    // A reload during a connection's connect made new entries, and the connection's success went onto the old one,
    // which nothing saves or reads any more
    [TestMethod]
    public async Task A_connections_outcome_lands_on_the_entry_a_reload_made_during_its_connect()
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

        var proxyOptions = new ProxyOptions { VerifyTls = false };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, store, socketFactory: null,
            serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The connection did not reach the proxy.");
        await connector.UpdateOptions(proxyOptions, TestCt);
        release.TrySetResult();
        using var connection = await connecting;

        Assert.AreEqual(2, connector.GetEndPointInfos().Single().Status.SucceededCount,
            "The connection's success went onto an entry the reload had replaced.");
    }

    // After the close, a flush wrote a status changed since to the closed store, and a check dialed the proxies. A
    // flush then saves nothing, as the VPN service's last status update flushes too, and a check is refused
    [TestMethod]
    public async Task A_closed_connector_saves_nothing_more_and_refuses_a_check()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        var store = new TestProxyEndPointStore(await CreateStore(ToProxyEndPoint(proxy.EndPoint)));
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store,
            socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await connector.DisposeAsync();

        // a failure reported after the close changes a status, which a write would now carry to the closed store
        connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));
        store.FailUpdates = true;
        await connector.Flush(TestCt);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connector.CheckServers(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, TestCt));
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
        var mgr = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(proxyEndPoint),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await mgr.CheckServers(socketFactory, (IPEndPoint)server.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        Assert.IsTrue(mgr.GetEndPointInfos().Single().Status.SucceededCount >= 1);
        await mgr.DisposeAsync();

        // a new manager over the same db restores the statuses
        var mgr2 = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(), socketFactory: null,
            serverCheckTimeout: null, TestCt);

        var restored = mgr2.GetEndPointInfos().Single();
        Assert.AreEqual(proxyEndPoint.Id, restored.EndPoint.Id);
        Assert.IsTrue(restored.Status.SucceededCount >= 1);
        Assert.IsNotNull(restored.Status.LastSucceeded);
        await mgr2.DisposeAsync();
    }

    // A list's reload during a check made new entries, and the check's outcomes went onto the old ones, which nothing
    // saves or reads any more: they now go onto the entries the connector holds
    [TestMethod]
    public async Task A_checks_outcomes_land_on_the_entries_a_reload_made_during_it()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var proxy = new TestSocks5Proxy().Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = ServeTlsOnceReleased(target, certificate, release.Task, TestCt);
        var proxyOptions = new ProxyOptions { VerifyTls = true };
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null,
            serverCheckTimeout: TimeSpan.FromSeconds(30), TestCt);

        var checking = connector.CheckServers(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint, hostName: null,
            serverCertificateValidation: null, TestCt);

        await AssertEqualsWait(1, () => proxy.Destinations.Count, "The check did not reach the server.");
        await connector.UpdateOptions(proxyOptions, TestCt);
        release.TrySetResult();
        await checking;
        await using var serverStream = await serving;

        Assert.AreEqual(1, connector.GetEndPointInfos().Single().Status.SucceededCount,
            "The check's success went onto an entry the reload had replaced.");
    }

    // a TLS server that holds its first connection's handshake until released
    private static async Task<SslStream> ServeTlsOnceReleased(TcpListener server, X509Certificate2 certificate,
        Task release, CancellationToken cancellationToken)
    {
        var client = await server.AcceptTcpClientAsync(cancellationToken);
        await release.WaitAsync(cancellationToken);
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate
        }, cancellationToken);

        return sslStream;
    }
}
