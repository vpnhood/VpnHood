using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Proxies.EndPointManagement.Sqlite;
using VpnHood.Core.Proxies.HttpProxyServers;
using VpnHood.Core.Proxies.Socks5ProxyServers;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;

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
}
