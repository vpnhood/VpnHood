using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Proxies.HttpProxyServers;
using VpnHood.Core.Proxies.Socks5ProxyServers;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyEndPointManagerTest : TestBase
{
    [TestMethod]
    public void IsEnabled_False_When_No_Servers()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyEndPointManager(new ProxyOptions(),
            storagePath: TestHelper.WorkingPath, socketFactory: socketFactory);
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_True_When_Servers_Exist()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyEndPointManager(
            proxyOptions: new ProxyOptions {
                ProxyEndPoints = [new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 }]
            },
            storagePath: TestHelper.WorkingPath,
            socketFactory: socketFactory);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyEndPointManager(new ProxyOptions(),
            storagePath: TestHelper.WorkingPath, socketFactory: socketFactory);
        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(async () =>
            await mgr.ConnectAsync(target, CancellationToken.None));
        Assert.AreEqual((int)SocketError.NetworkUnreachable, ex.ErrorCode);
    }

    [TestMethod]
    public void Constructor_Supports_Multiple_Proxy_Types()
    {
        var socketFactory = new TestSocketFactory();
        var proxyEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "127.0.0.1", Port = 8080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Https, Host = "127.0.0.1", Port = 8443 }
        };

        var proxyOptions = new ProxyOptions { ProxyEndPoints = proxyEndPoints };
        var mgr = new ProxyEndPointManager(proxyOptions: proxyOptions, storagePath: TestHelper.WorkingPath,
            socketFactory: socketFactory);

        Assert.IsTrue(mgr.IsEnabled);
        Assert.HasCount(4, mgr.Status.ProxyEndPointInfos);
    }

    [TestMethod]
    public async Task RemoveBadServers_Marks_Unsupported_Types_As_Inactive()
    {
        var socketFactory = new TestSocketFactory();
        var proxyEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "127.0.0.1", Port = 8080 }
        };

        var proxyOptions = new ProxyOptions { ProxyEndPoints = proxyEndPoints };
        var mgr = new ProxyEndPointManager(proxyOptions: proxyOptions, storagePath: TestHelper.WorkingPath,
            socketFactory: socketFactory);
        await mgr.RemoveBadServers(CancellationToken.None);

        // All non-SOCKS5 servers should be marked as inactive
        Assert.IsTrue(mgr.Status.ProxyEndPointInfos.All(status => !status.EndPoint.IsEnabled));
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

        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.AllowTcpReuse = false; // make sure not reuse connections
        clientOptions.ProxyOptions = new ProxyOptions {
            ProxyEndPoints = [
                new ProxyEndPoint {
                        Protocol = ProxyProtocol.Socks5,
                        Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
                        Port = socks5ProxyServer.ListenerEndPoint.Port },
                    new ProxyEndPoint {
                        Protocol = ProxyProtocol.Http,
                        Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
                        Port = httpProxyServer.ListenerEndPoint.Port }
            ]
        }; // set to proxies end point
        var clientServerDom = await ClientServerDom.Create(TestHelper, clientOptions);

        await TestHelper.Test_Https();

        var proxyEndPointInfos = clientServerDom.Client.ProxyEndPointManager.Status.ProxyEndPointInfos;
        Assert.IsTrue(proxyEndPointInfos.All(x => x.Status.SucceededCount >= 1));
    }
}
