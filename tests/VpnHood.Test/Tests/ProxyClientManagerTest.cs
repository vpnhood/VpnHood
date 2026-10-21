using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Net.Proxies.HttpProxyServers;
using VpnHood.Net.Proxies.Socks5ProxyServers;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.Test.Tests;

[TestClass]
public class ManagedProxyConnectorTest : TestBase
{
    private async Task<ProxyEndPointStore> CreateStore(params ProxyEndPoint[] proxyEndPoints)
    {
        var store = new ProxyEndPointStore(Path.Combine(TestHelper.WorkingPath, "proxies", "proxies.db"));
        if (proxyEndPoints.Length > 0)
            await store.Upsert(proxyEndPoints.Select(x => new ProxyEndPointRecord { EndPoint = x }).ToArray());
        return store;
    }

    // the client derives the same db path from its storage folder
    private string ClientStoreDbPath =>
        Path.Combine(TestHelper.WorkingPath, "ClientCore", "proxies", "proxies.db");

    [TestMethod]
    public async Task IsEnabled_False_When_No_Servers()
    {
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore());
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task IsEnabled_True_When_Servers_Exist()
    {
        var store = await CreateStore(
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), await CreateStore());
        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(async () =>
            await mgr.ConnectAsync(socketFactory, target, null, TestCt));
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

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store);

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
        // Create two TCP listeners that simulate non-HTTP proxy servers by returning invalid responses
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
                Protocol = ProxyProtocol.Http,
                Host = endPoint1.Address.ToString(),
                Port = endPoint1.Port
            },
            new ProxyEndPoint {
                Protocol = ProxyProtocol.Http,
                Host = endPoint2.Address.ToString(),
                Port = endPoint2.Port
            });

        var mgr = await ManagedProxyConnector.Create(new ProxyOptions(), store);
        await mgr.CheckServers(socketFactory, TestCt);

        // All non-SOCKS5 servers should be marked as inactive
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
        var connector = await ManagedProxyConnector.Create(new ProxyOptions { VerifyTls = false }, store);
        using var tcpClient = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, null, TestCt);
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
        TimeSpan? serverCheckTimeout = null)
    {
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();

        var store = await CreateStore();
        await store.Upsert([
            new ProxyEndPointRecord {
                EndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Socks5,
                    Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = socks5ProxyServer.ListenerEndPoint.Port
                },
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ]);
        var connector = await ManagedProxyConnector.Create(new ProxyOptions(), store, serverCheckTimeout);
        var tcpClient = await connector.ConnectAsync(new TestSocketFactory(),
            (IPEndPoint)target.LocalEndpoint, null, TestCt);
        return (connector, tcpClient);
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

        // first manager records a successful check and flushes on dispose
        var mgr = await ManagedProxyConnector.Create(proxyOptions, await CreateStore(proxyEndPoint));
        await mgr.CheckServers(socketFactory, TestCt);
        Assert.IsTrue(mgr.GetEndPointInfos().Single().Status.SucceededCount >= 1);
        await mgr.DisposeAsync();

        // a new manager over the same db restores the statuses
        var mgr2 = await ManagedProxyConnector.Create(proxyOptions, await CreateStore());
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
            await store.Upsert([
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
                await ManagedProxyConnector.Create(CreateAutoUpdateOptions(port, "/a"), await CreateStore());
            await AssertEqualsWait(1, () => downloads.GetValueOrDefault("/a"), "the first download");

            // the same options again: no download
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/a"));
            await Task.Delay(TimeSpan.FromSeconds(4), TestCt);
            Assert.AreEqual(1, downloads.GetValueOrDefault("/a"), "no download for the same options");

            // a new url: a download at once
            await connector.UpdateOptions(CreateAutoUpdateOptions(port, "/b"));
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
