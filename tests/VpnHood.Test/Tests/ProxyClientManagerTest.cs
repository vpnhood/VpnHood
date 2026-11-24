using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using VpnHood.AppLogic.Api.WebHost.Helpers;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Checks;
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
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task IsEnabled_True_When_Servers_Exist()
    {
        var store = await CreateStore(
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null, serverCheckTimeout: null, TestCt);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);
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

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null, serverCheckTimeout: null, TestCt);

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

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store, socketFactory: null, serverCheckTimeout: null, TestCt);
        await mgr.CheckServers(socketFactory, new IPEndPoint(IPAddress.Loopback, 443), hostName: null, serverCertificateValidation: null, TestCt);

        // All servers that are no SOCKS4 proxy should be marked as inactive
        Assert.IsTrue(mgr.GetEndPointInfos().All(status => !status.EndPoint.IsEnabled));
    }

    // A connector whose one proxy has a status its store has not saved yet: a connect through it succeeded. The proxy
    // succeeded last, so the connect checks nothing, and no check behind it saves the status first
    private async Task<(ManagedProxyConnector Connector, TestProxyEndPointStore Store)> CreateConnectorWithUnsavedStatus()
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
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store, socketFactory: null, serverCheckTimeout: null, TestCt);
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
            socketFactory: null, serverCheckTimeout, TestCt);

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
            (IPEndPoint)target.LocalEndpoint, hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        // the later connection checked, and a pass of the check behind it would land between the read and the assert
        await AssertEqualsWait(true, () => connector.Progress is null, "The check behind the connection did not end.");
        var status = connector.GetEndPointInfos().Single().Status;
        var succeededCount = status.SucceededCount;
        connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));

        Assert.AreEqual(succeededCount, status.SucceededCount);
        Assert.AreEqual(1, status.FailedCount);
    }

    // A connection whose dial ended while a reset's reload was under way read the reset count before the reload's end,
    // which counted the reset, and put its success on an entry the reload made: its failure after then took nothing back,
    // and the proxy kept a success it never had. The count now comes from the entry that takes the success
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
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldListCount), "The reset's reload did not start.");
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
            Assert.AreEqual(succeededCount, status.SucceededCount, "The failure took back the later connection's success.");
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

        // both succeeded last, the first first in turn; both on this machine's address, so a success turns the other off
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
            await AssertEqualsWait(1, () => Volatile.Read(ref store.HeldResetCount), "The connector's reset did not start.");
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
    // connection's success on one, with the reset's count, went with them, and its failure after would take back a later
    // connection's. The entries the reload made stay now
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
            Assert.AreEqual(1, status.SucceededCount, "The entry the reload made, with the connection's success, went.");
            connector.RecordFailed(connection.TcpClient, new AuthenticationException("The TLS handshake failed."));
            Assert.AreEqual(0, status.SucceededCount);
        }
        finally {
            store.ReleaseQueuePositionReads();
            release.TrySetResult();
        }
    }

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

    // After the close, a flush wrote a status changed since to the closed store, and a check dialed the proxies. A flush
    // then saves nothing, as the VPN service's last status update flushes too, and a check is refused
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
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

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
            await CreateStore(ToProxyEndPoint(proxy.EndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

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
            socketFactory: null, serverCheckTimeout: null, TestCt);

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

    // the system's sockets, with a call before each TCP client it makes, given the endpoint the client is for
    private sealed class DialHookSocketFactory(Action<IPEndPoint> onCreate) : TestSocketFactory
    {
        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            onCreate(ipEndPoint);
            return base.CreateTcpClient(ipEndPoint, options);
        }
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

    // On iOS and tvOS the check behind a connect dialed 3 at a time beside the session, so a list whose proxies mostly
    // never answer took minutes to rate, each holding its place for the check's whole time. It now dials as many at once
    // as before its pass, as only its TLS handshakes cost an extension's memory: here two, the run's width, where one
    // handshake at a time is the width beside the session; and one whose check ends gives its place to the next
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

    // A connector whose check before a connect passed at two answering proxies, which took the run's two places at once,
    // and goes on through the silent ones behind the connect, two of them dialed and the third waiting for a place: its
    // width is two, and one TLS handshake at a time beside the session
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

    // checks with TLS; the test proxies are all on this machine's address, which the rule of one endpoint per address
    // would leave to one
    private static ProxyOptions TlsCheckOptions => new() {
        VerifyTls = true,
        AutoUpdateOptions = new ProxyAutoUpdateOptions { RemoveDuplicateIps = false }
    };

    // A connector that checks with TLS, one handshake at a time beside the session, whose store has one proxy that
    // worked last: a connection through it gives the checks behind the session their server
    private async Task<ManagedProxyConnector> CreateConnectorWithAWorkingProxy(TestSocks5Proxy proxy,
        TimeSpan serverCheckTimeout)
    {
        var store = await CreateStore();
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(proxy.EndPoint),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);

        var connector = await ManagedProxyConnector.Create(TlsCheckOptions, store, socketFactory: null,
            serverCheckTimeout, TestCt);

        connector.HandshakePlaces = new SemaphoreSlim(1);
        connector.ConnectionHandshakePlaces = new SemaphoreSlim(1);
        return connector;
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
        Assert.AreEqual(0, vpnServer.HelloTimes.Count, "The connection's own check made its handshake without its place.");

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

    // A check that waited for its turn at the handshakes went on with the credentials its proxy had when the check began,
    // or was dropped where the user changed them meanwhile, so a corrected proxy kept the failure of its wrong password:
    // the run goes again over those never rated alone. It takes those the proxy has at its turn
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

    // A check that came before a proxy reached a server took no turn for the rest of its run, though the checks that came
    // after went beside the session: the server search's other probes went on 50 at a time. The gate reads it as each
    // check comes
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

    // A VPN server that answers each TLS hello after a pause, the time a handshake takes it, and closes a connection that
    // sends none within the limit from its accept, as the real one does after 10 seconds. It counts the connections it
    // takes and notes when each hello came; with HoldsFirst, the first hello waits for the test's release besides
    private sealed class PausingTlsServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly X509Certificate2 _certificate = CertificateUtil.CreateSelfSigned();
        private readonly TaskCompletionSource _firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<Task> _answers = new();
        private Task _accepting = Task.CompletedTask;
        private int _acceptedCount;
        private int _helloCount;

        public TimeSpan Pause { get; init; }
        public TimeSpan HelloLimit { get; init; } = Timeout.InfiniteTimeSpan;
        public bool HoldsFirst { get; init; }
        public ConcurrentQueue<long> HelloTimes { get; } = new();
        public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;
        public int AcceptedCount => Volatile.Read(ref _acceptedCount);

        public PausingTlsServer Start()
        {
            _listener.Start();
            _accepting = Accept(_cts.Token);
            return this;
        }

        public void ReleaseFirst()
        {
            _firstRelease.TrySetResult();
        }

        private async Task Accept(CancellationToken cancellationToken)
        {
            try {
                while (true) {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    Interlocked.Increment(ref _acceptedCount);
                    _answers.Enqueue(Answer(client, cancellationToken));
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // the server's close
            }
        }

        // its own client, closed at its end
        private async Task Answer(TcpClient client, CancellationToken cancellationToken)
        {
            try {
                // the hello, left for the handshake to read
                using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                helloCts.CancelAfter(HelloLimit);
                if (await client.Client.ReceiveAsync(new byte[1], SocketFlags.Peek, helloCts.Token) == 0)
                    return;

                HelloTimes.Enqueue(Environment.TickCount64);
                if (HoldsFirst && Interlocked.Increment(ref _helloCount) == 1)
                    await _firstRelease.Task.WaitAsync(cancellationToken);

                await Task.Delay(Pause, cancellationToken);
                await using var sslStream = new SslStream(client.GetStream());
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = _certificate }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException
                                           or OperationCanceledException) {
                // no hello within the limit, or a client that went before its handshake's end
            }
            finally {
                client.Dispose();
            }
        }

        // after its accept loop's end and every answer's, which its token ends, so no handshake has the certificate
        public void Dispose()
        {
            _cts.Cancel();
            _accepting.GetAwaiter().GetResult();
            Task.WaitAll(_answers.ToArray());
            _listener.Stop();
            _certificate.Dispose();
            _cts.Dispose();
        }
    }

    // A proxy that takes each connection and answers nothing, as one that hangs. It holds them for the test, which can
    // close one to fail its dial at once
    private sealed class SilentProxy : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<TcpClient> _accepted = Channel.CreateUnbounded<TcpClient>();
        private readonly ConcurrentQueue<TcpClient> _connections = new();
        private Task _accepting = Task.CompletedTask;

        public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;
        public int ConnectionCount => _connections.Count;

        // the connections it took that sent something, as a TLS hello, which it never reads
        public int HelloCount => _connections.Count(HasData);

        public SilentProxy Start()
        {
            _listener.Start();
            _accepting = Accept(_cts.Token);
            return this;
        }

        private async Task Accept(CancellationToken cancellationToken)
        {
            try {
                while (true) {
                    var connection = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _connections.Enqueue(connection);
                    _accepted.Writer.TryWrite(connection);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // the proxy's close
            }
        }

        private static bool HasData(TcpClient connection)
        {
            try {
                return connection.Available > 0;
            }
            catch (ObjectDisposedException) {
                // one the test closed
                return false;
            }
        }

        // the connections it took, each once, in turn
        public ValueTask<TcpClient> TakeConnection(CancellationToken cancellationToken) =>
            _accepted.Reader.ReadAsync(cancellationToken);

        // the connections that sent something closed, so the checks that hold them fail at once
        public void CloseHelloConnections()
        {
            foreach (var connection in _connections.Where(HasData).ToArray())
                connection.Dispose();
        }

        // after its accept loop's end, which its token ends, so no connection it takes at the close is left open
        public void Dispose()
        {
            _cts.Cancel();
            _accepting.GetAwaiter().GetResult();
            _listener.Stop();
            _accepted.Writer.TryComplete();
            foreach (var connection in _connections)
                connection.Dispose();

            _cts.Dispose();
        }
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

    // the system's sockets, the one of the number given made only after the time given, as a VPN's factory may take
    // long to keep a socket out of the VPN
    private sealed class SlowSocketFactory(TimeSpan delay, int slowSocketNumber) : TestSocketFactory
    {
        private int _socketCount;

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            if (Interlocked.Increment(ref _socketCount) == slowSocketNumber)
                Thread.Sleep(delay);

            return base.CreateTcpClient(ipEndPoint, options);
        }
    }

    // the system's sockets, each made, while it holds, only once the test lets them: a window before a check's dial
    private sealed class HeldSocketFactory(CancellationToken cancellationToken) : TestSocketFactory
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsHolding { get; set; }
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            if (IsHolding) {
                Held.TrySetResult();
                _release.Task.Wait(cancellationToken);
            }

            return base.CreateTcpClient(ipEndPoint, options);
        }
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
            socketFactory: null, serverCheckTimeout: null, TestCt);

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

        public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) => Task.FromResult(true);

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

        public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) => Task.FromResult(true);

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
        var mgr = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(proxyEndPoint), socketFactory: null, serverCheckTimeout: null, TestCt);
        await mgr.CheckServers(socketFactory, (IPEndPoint)server.LocalEndpoint, hostName: null, serverCertificateValidation: null, TestCt);
        Assert.IsTrue(mgr.GetEndPointInfos().Single().Status.SucceededCount >= 1);
        await mgr.DisposeAsync();

        // a new manager over the same db restores the statuses
        var mgr2 = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);
        var restored = mgr2.GetEndPointInfos().Single();
        Assert.AreEqual(proxyEndPoint.Id, restored.EndPoint.Id);
        Assert.IsTrue(restored.Status.SucceededCount >= 1);
        Assert.IsNotNull(restored.Status.LastSucceeded);
        await mgr2.DisposeAsync();
    }

    // Each protocol on its own: with both in one list, the connect goes on at the first to pass, and the session's few
    // connections may take that one alone
    [TestMethod]
    [DataRow(ProxyProtocol.Socks5)]
    [DataRow(ProxyProtocol.Http)]
    public async Task Connect_via_proxy(ProxyProtocol protocol)
    {
        // a local proxy of the protocol
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        IPEndPoint proxyEndPoint;
        if (protocol == ProxyProtocol.Socks5) {
            socks5ProxyServer.Start();
            proxyEndPoint = socks5ProxyServer.ListenerEndPoint;
        }
        else {
            httpProxyServer.Start();
            proxyEndPoint = httpProxyServer.ListenerEndPoint;
        }

        // seed the shared endpoint store the client will open
        using (var store = new ProxyEndPointStore(ClientStoreDbPath)) {
            await Upsert(store, [
                new ProxyEndPointRecord {
                    EndPoint = new ProxyEndPoint {
                        Protocol = protocol,
                        Host = proxyEndPoint.Address.ToString(),
                        Port = proxyEndPoint.Port
                    }
                }
            ]);
        }

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowChannelReuse = false; // make sure not reuse connections
        var socketFactory = new EndPointRecordingSocketFactory();
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions,
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Managed }, clientSocketFactory: socketFactory);

        await TestHelper.Test_Https();

        // Connections through the proxy, which the server search's probe makes too, and none straight to the server,
        // the session's own included
        var mgr = clientServerDom.Client.ProxyConnector as ManagedProxyConnector;
        Assert.IsNotNull(mgr);
        Assert.IsGreaterThan(0, mgr.Status.SessionStatus.SucceededCount, "No connection went through the proxy.");
        AssertNoDialTo(clientServerDom.Token, socketFactory);
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
        var port = GetFreePort();
        var webServer = new WebserverLite(new WebserverSettings(IPAddress.Loopback.ToString(), port), async ctx => {
            downloads.AddOrUpdate(ctx.Request.Url.RawWithoutQuery, 1, (_, count) => count + 1);
            await ctx.Response.Send("socks5://127.0.0.1:1080");
        });
        _ = webServer.StartAsync(TestCt);
        try {
            await using var connector =
                await ManagedProxyConnector.Create(CreateAutoUpdateOptions(port, "/a"), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

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

    // A list with no endpoint yet, before its first download: the VPN went straight to its server, around the proxies
    // the user had turned on. A connection now waits for the list, held back here, and goes through its proxy; one
    // whose caller gives up meanwhile ends with its cancellation
    [TestMethod]
    public async Task A_connection_to_a_list_with_no_endpoint_yet_waits_for_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var targetEndPoint = (IPEndPoint)target.LocalEndpoint;
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);

        // an hour between downloads: the first one at once all the same, not at the job runner's next check
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromHours(1)), await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        Assert.IsFalse(connector.IsEnabled, "The list has an endpoint before its download.");
        using var givingUpCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var givingUp = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, givingUpCts.Token);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), targetEndPoint, hostName: null,
            serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start at once.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(connecting.IsCompleted, "A connection went on before the list came.");

        await givingUpCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => givingUp);

        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint,
            "The connection went around the list's proxy.");
    }

    // The wait was the server search's own probe's, five seconds by default, so a list that came later never carried a
    // session, and the failed connect's close ended its download. The connect now waits for it under its own time: the
    // clock here starts once the client waits, at the proxies' step, or at the search's, where the old code waited
    [TestMethod]
    public async Task A_list_that_comes_after_the_servers_probe_time_still_carries_the_session()
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        socks5ProxyServer.Start();
        using var listServer = new ListServer($"socks5://{socks5ProxyServer.ListenerEndPoint}", isHeld: true, TestCt);
        await using var server = await TestHelper.CreateServer();
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.Transport.ServerQueryTimeout = TimeSpan.FromSeconds(3);
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: new TestNullVpnAdapter(),
            autoConnect: false, proxyOptions: CreateManagedListOptions(listServer.Url));

        var connecting = client.Connect(TestCt);
        await AssertEqualsWait(true, () => client.State is ClientState.ValidatingProxies or
            ClientState.FindingReachableServer, "The client did not come to its wait.");

        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start.");
        await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
        listServer.Release();
        await connecting;

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsNotNull(client.ProxyConnector);
        Assert.IsGreaterThan(0, client.ProxyConnector.Status.SessionStatus.SucceededCount,
            "The session went around the list's proxy.");
    }

    // A list that had only just come was left to the server search's first probe to check, within the probe's own few
    // seconds, which a slow proxy's check and then its connection took whole: the connect said that the server could
    // not be reached. It is now checked before the search, under the connect's own time, until a proxy passes; a
    // silent one beside it, whose check goes on behind the connect, is no reason for the search to check again
    [TestMethod]
    public async Task A_slow_proxy_of_a_list_that_has_only_just_come_still_carries_the_session()
    {
        using var proxy = new TestSocks5Proxy { ConnectDelay = TimeSpan.FromSeconds(3) }.Start();
        using var silentProxy = new TcpListener(IPAddress.Loopback, 0); // it takes a connection and answers nothing
        silentProxy.Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}\nsocks5://{silentProxy.LocalEndpoint}",
            isHeld: true, TestCt);

        await using var server = await TestHelper.CreateServer();
        var clientOptions = TestHelper.CreateClientOptions(await TestHelper.CreateAccessToken(server));
        clientOptions.Transport.ServerQueryTimeout = TimeSpan.FromSeconds(6);
        clientOptions.Transport.TcpConnectTimeout = TimeSpan.FromSeconds(10); // the tests' 3 s is the proxy's own time
        await using var client = await TestHelper.CreateClient(clientOptions, vpnAdapter: new TestNullVpnAdapter(),
            autoConnect: false, proxyOptions: CreateManagedListOptions(listServer.Url));

        // the list comes once the client waits for it, so the client never finds one at its start
        var connecting = client.Connect(TestCt);
        await AssertEqualsWait(ClientState.ValidatingProxies, () => client.State);
        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list's first download did not start.");
        listServer.Release();
        await connecting;

        Assert.AreEqual(ClientState.Connected, client.State);
        Assert.IsNotNull(client.ProxyConnector);
        Assert.IsGreaterThan(0, client.ProxyConnector.Status.SessionStatus.SucceededCount,
            "The session went around the list's proxy.");
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

        await AssertEqualsWait(1, () => slowProxy.Destinations.Count, "The first connection's check missed the slow one.");
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

    // A list whose download brings no proxy: the connect says that no proxy can be reached, and dials nothing else
    [TestMethod]
    public async Task A_list_that_brings_no_proxy_ends_the_connect_with_no_proxy_reached()
    {
        using var listServer = new ListServer("no proxy here", isHeld: false, TestCt);
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var socketFactory = new EndPointRecordingSocketFactory();
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: CreateManagedListOptions(listServer.Url), socketFactory: socketFactory));

        Assert.AreEqual(1, listServer.DownloadCount);
        AssertNoDialTo(token, socketFactory);
    }

    // A key known by name alone, with a list that brings no proxy: the connect went on to look the server's name up,
    // and its failed lookup said that the server could not be found. It now says that no proxy can be reached
    [TestMethod]
    public async Task A_key_known_by_name_alone_with_no_proxy_to_use_ends_with_no_proxy_reached()
    {
        using var listServer = new ListServer("no proxy here", isHeld: false, TestCt);
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        token.ServerToken.HostName = "proxied-server.invalid";
        token.ServerToken.IsValidHostName = true;
        token.ServerToken.HostEndPoints = null;

        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: CreateManagedListOptions(listServer.Url)));
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

    // With the update interval "never", the app's default, nothing downloaded the list, and a connection to it, empty,
    // failed every time. Its first download now comes all the same, as the app leaves it to the connector
    [TestMethod]
    public async Task A_list_with_no_update_interval_still_has_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: false, TestCt);
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url, interval: null),
            await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        using var connection = await connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        Assert.AreEqual(1, listServer.DownloadCount);
    }

    // A list whose endpoints are all turned off has none to use either: it waits for its download as an empty one does,
    // where it failed at once
    [TestMethod]
    public async Task A_list_with_every_endpoint_turned_off_waits_for_its_first_download()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        await using var connector = await ManagedProxyConnector.Create(
            CreateListOptions(listServer.Url, TimeSpan.FromHours(1)),
            await CreateStore(ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1), isEnabled: false)),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        Assert.IsFalse(connecting.IsCompleted, "The connection failed before the list came.");
        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // New options during the wait: the old download's end let the waiting connections into the empty list, which then
    // failed while the new one was coming. They now wait for the new one
    [TestMethod]
    public async Task A_connection_waiting_for_a_list_follows_its_replacement()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var oldListServer = new ListServer("never sent", isHeld: true, TestCt);
        using var newListServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        await using var connector = await ManagedProxyConnector.Create(
            CreateListOptions(oldListServer.Url, TimeSpan.FromHours(1)), await CreateStore(), socketFactory: null,
            serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => oldListServer.DownloadCount);
        await connector.UpdateOptions(CreateListOptions(newListServer.Url, TimeSpan.FromHours(1)), TestCt);
        await AssertEqualsWait(1, () => newListServer.DownloadCount);
        Assert.IsFalse(connecting.IsCompleted, "The connection failed before the new list came.");

        newListServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // A connection waiting for the list goes on with an endpoint the app adds meanwhile, with the options it sends then
    [TestMethod]
    public async Task A_connection_waiting_for_the_list_takes_an_endpoint_the_app_adds()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer("never sent", isHeld: true, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, TimeSpan.FromHours(1));
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        using var appStore = await CreateStore(ToProxyEndPoint(proxy.EndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);

        using var connection = await connecting.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
    }

    // A reload that brought an enabled endpoint marked the list's download as done, so a connection made after the
    // endpoint was turned off again failed at once, while the list was still coming. A reload now only wakes the
    // waiting connections, which look again; and the endpoint's going off started the download still coming again,
    // which the gate's one release then let through a second time
    [TestMethod]
    public async Task A_connection_still_waits_for_the_list_after_an_endpoint_came_and_went()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var proxy = new TestSocks5Proxy().Start();
        using var listServer = new ListServer($"socks5://{proxy.EndPoint}", isHeld: true, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, TimeSpan.FromHours(1));
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);

        // the app adds an endpoint, then turns it off, and sends its options each time
        var appEndPoint = new IPEndPoint(IPAddress.Loopback, 1);
        using var appStore = await CreateStore(ToProxyEndPoint(appEndPoint));
        await connector.UpdateOptions(proxyOptions, TestCt);
        await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(appEndPoint, isEnabled: false) }]);
        await connector.UpdateOptions(proxyOptions, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), (IPEndPoint)target.LocalEndpoint,
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsFalse(connecting.IsCompleted, "The connection ended before the list came.");
        listServer.Release();
        using var connection = await connecting;
        Assert.AreEqual(proxy.EndPoint, connection.TcpClient.Client.RemoteEndPoint);
        Assert.AreEqual(1, listServer.DownloadCount, "The download still coming was started again.");
    }

    // A list with no update interval whose last enabled endpoint the app turns off has nothing to use: it downloads the
    // list once, as at its start, where it did nothing
    [TestMethod]
    public async Task A_list_with_no_interval_whose_last_endpoint_goes_off_downloads_again()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, interval: null);
        var storedEndPoint = new IPEndPoint(IPAddress.Loopback, 1);
        await using var connector = await ManagedProxyConnector.Create(proxyOptions,
            await CreateStore(ToProxyEndPoint(storedEndPoint)), socketFactory: null, serverCheckTimeout: null, TestCt);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.AreEqual(0, listServer.DownloadCount, "A list with an endpoint to use and no interval was downloaded.");

        using var appStore = await CreateStore();
        await Upsert(appStore, [new ProxyEndPointRecord { EndPoint = ToProxyEndPoint(storedEndPoint, isEnabled: false) }]);
        await connector.UpdateOptions(proxyOptions, TestCt);
        await AssertEqualsWait(1, () => listServer.DownloadCount, "The list was not downloaded again.");
    }

    // A list with no update interval downloaded once, at its start: once the user turned off the endpoint it brought,
    // nothing was left to use, and the one download, done, kept any other from starting
    [TestMethod]
    public async Task A_list_with_no_interval_downloads_again_when_its_downloaded_endpoint_goes_off()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var proxyOptions = CreateListOptions(listServer.Url, interval: null);
        await using var connector = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(),
            socketFactory: null, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(true, () => connector.IsEnabled, "The list did not come.");
        using var appStore = await CreateStore();
        await Upsert(appStore, [
            new ProxyEndPointRecord {
                EndPoint = ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1080), isEnabled: false)
            }
        ]);

        await connector.UpdateOptions(proxyOptions, TestCt);
        await AssertEqualsWait(2, () => listServer.DownloadCount, "The list was not downloaded again.");
    }

    [TestMethod]
    public async Task The_close_ends_a_connection_waiting_for_the_list()
    {
        using var listServer = new ListServer("never sent", isHeld: true, TestCt);
        var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url, TimeSpan.FromHours(1)),
            await CreateStore(), socketFactory: null, serverCheckTimeout: null, TestCt);

        var connecting = connector.ConnectAsync(new TestSocketFactory(), new IPEndPoint(IPAddress.Loopback, 443),
            hostName: null, serverCertificateValidation: null, onAttempt: null, TestCt);

        await AssertEqualsWait(1, () => listServer.DownloadCount);
        await connector.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connecting);
    }

    // Under the kill switch's block, which lets only the VPN's own traffic out, a download over the system's sockets
    // and resolver could not reach the list: it now goes over the VPN's socket factory
    [TestMethod]
    public async Task The_lists_download_goes_over_the_VPNs_socket_factory()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromHours(1)), await CreateStore(), socketFactory, serverCheckTimeout: null, TestCt);

        await connector.WaitForEndPoints(TestCt);

        Assert.IsTrue(connector.IsEnabled, "The list did not come.");
        Assert.Contains(new IPEndPoint(IPAddress.Loopback, listServer.Port), socketFactory.EndPoints,
            "The download went around the socket factory.");
    }

    // The update's later downloads come once the tunnel is up, and go the platform's way, not over the VPN's sockets
    [TestMethod]
    public async Task The_lists_later_downloads_leave_the_VPNs_socket_factory_alone()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(CreateListOptions(listServer.Url,
            TimeSpan.FromSeconds(1)), await CreateStore(), socketFactory, serverCheckTimeout: null, TestCt);

        await AssertEqualsWait(true, () => listServer.DownloadCount >= 2, "The list was not downloaded again.",
            timeout: 15000);

        Assert.AreEqual(1, socketFactory.EndPoints.Count(x => x.Port == listServer.Port),
            "A later download went over the socket factory.");
    }

    // The VPN's sockets, kept since the connector's start for its first download, served a list whose URL the app's
    // options gave once the session was up, and its download went around the tunnel: they serve the update the
    // connector starts with alone
    [TestMethod]
    public async Task A_list_the_options_bring_later_leaves_the_VPNs_socket_factory_alone()
    {
        using var listServer = new ListServer("socks5://127.0.0.1:1080", isHeld: false, TestCt);
        var socketFactory = new EndPointRecordingSocketFactory();
        await using var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false },
            await CreateStore(ToProxyEndPoint(new IPEndPoint(IPAddress.Loopback, 1081))), socketFactory,
            serverCheckTimeout: null, TestCt);

        await connector.UpdateOptions(CreateListOptions(listServer.Url, TimeSpan.FromHours(1)), TestCt);
        await AssertEqualsWait(1, () => listServer.DownloadCount);
        Assert.IsEmpty(socketFactory.EndPoints, "The download went over the socket factory.");
    }

    // With the user's proxies on and none in the list, the VPN connected straight to the server; it now says that no
    // proxy can be reached, and dials nothing
    [TestMethod]
    public async Task With_proxies_on_and_none_in_the_list_the_VPN_never_goes_straight_to_the_server()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var socketFactory = new EndPointRecordingSocketFactory();
        await Assert.ThrowsExactlyAsync<UnreachableProxyServerException>(() => TestHelper.CreateClient(
            TestHelper.CreateClientOptions(token), vpnAdapter: new TestNullVpnAdapter(),
            proxyOptions: new ProxyOptions { Mode = ProxyMode.Managed }, socketFactory: socketFactory));

        AssertNoDialTo(token, socketFactory);
    }

    // no TCP connection made to any of the key's server endpoints, as a connection around the proxies would be
    private static void AssertNoDialTo(Token token, EndPointRecordingSocketFactory socketFactory)
    {
        var serverEndPoints = token.ServerToken.HostEndPoints ?? [];
        Assert.IsNotEmpty(serverEndPoints);
        Assert.IsFalse(socketFactory.EndPoints.Any(serverEndPoints.Contains), "The VPN went straight to its server.");
    }

    private static ProxyOptions CreateListOptions(Uri url, TimeSpan? interval)
    {
        return new ProxyOptions {
            VerifyTls = false, // the targets speak no TLS
            AutoUpdateOptions = new ProxyAutoUpdateOptions { Url = url, Interval = interval }
        };
    }

    // the client's managed list, from the URL alone, with an hour between downloads
    private static ProxyOptions CreateManagedListOptions(Uri url)
    {
        return new ProxyOptions {
            Mode = ProxyMode.Managed,
            AutoUpdateOptions = new ProxyAutoUpdateOptions { Url = url, Interval = TimeSpan.FromHours(1) }
        };
    }

    private sealed class EndPointRecordingSocketFactory : TestSocketFactory
    {
        public ConcurrentQueue<IPEndPoint> EndPoints { get; } = new();

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            EndPoints.Enqueue(ipEndPoint);
            return base.CreateTcpClient(ipEndPoint, options);
        }
    }

    // A list's URL on this machine: it answers with its content, at once or once released, and counts the downloads
    private sealed class ListServer : IDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WebserverLite _webServer;
        private int _downloadCount;

        public int Port { get; } = GetFreePort();

        public ListServer(string content, bool isHeld, CancellationToken cancellationToken)
        {
            if (!isHeld)
                _release.TrySetResult();

            _webServer = new WebserverLite(new WebserverSettings(IPAddress.Loopback.ToString(), Port), async ctx => {
                Interlocked.Increment(ref _downloadCount);
                await _release.Task.WaitAsync(cancellationToken);
                await ctx.Response.Send(content);
            });

            _ = _webServer.StartAsync(cancellationToken);
        }

        public Uri Url => new($"http://127.0.0.1:{Port}/list");
        public int DownloadCount => Volatile.Read(ref _downloadCount);
        public void Release() => _release.TrySetResult();

        // a download still held ends with the server
        public void Dispose()
        {
            _release.TrySetCanceled();
            _webServer.TryStop();
        }
    }

    private static int GetFreePort()
    {
        using var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start();
        return ((IPEndPoint)portFinder.LocalEndpoint).Port;
    }

    // nine seconds between downloads, the first one at once
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
