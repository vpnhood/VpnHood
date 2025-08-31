using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ProxyServers;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyServerManagerTest
{
    [TestMethod]
    public void IsEnabled_False_When_No_Servers()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyServerManager(proxyServerEndPoints: [], socketFactory: socketFactory);
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_True_When_Servers_Exist()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyServerManager(
            proxyServerEndPoints: [new ProxyServerEndPoint { Type = ProxyServerType.Socks5, Host = "127.0.0.1", Port = 1080 }],
            socketFactory: socketFactory);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyServerManager(proxyServerEndPoints: [], socketFactory: socketFactory);
        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(
            async () => await mgr.ConnectAsync(target, CancellationToken.None));
        Assert.AreEqual((int)SocketError.NetworkUnreachable, ex.ErrorCode);
    }

    [TestMethod]
    public void Constructor_Supports_Multiple_Proxy_Types()
    {
        var socketFactory = new TestSocketFactory();
        var proxyEndPoints = new[]
        {
            new ProxyServerEndPoint { Type = ProxyServerType.Socks5, Host = "127.0.0.1", Port = 1080 },
            new ProxyServerEndPoint { Type = ProxyServerType.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyServerEndPoint { Type = ProxyServerType.Http, Host = "127.0.0.1", Port = 8080 },
            new ProxyServerEndPoint { Type = ProxyServerType.Https, Host = "127.0.0.1", Port = 8443 }
        };

        var mgr = new ProxyServerManager(proxyServerEndPoints: proxyEndPoints, socketFactory: socketFactory);
        Assert.IsTrue(mgr.IsEnabled);
        Assert.AreEqual(4, mgr.ProxyServerStatuses.Length);
    }

    [TestMethod]
    public async Task RemoveBadServers_Marks_Unsupported_Types_As_Inactive()
    {
        var socketFactory = new TestSocketFactory();
        var proxyEndPoints = new[]
        {
            new ProxyServerEndPoint { Type = ProxyServerType.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyServerEndPoint { Type = ProxyServerType.Http, Host = "127.0.0.1", Port = 8080 }
        };

        var mgr = new ProxyServerManager(proxyServerEndPoints: proxyEndPoints, socketFactory: socketFactory);
        await mgr.RemoveBadServers(CancellationToken.None);

        // All non-SOCKS5 servers should be marked as inactive
        Assert.IsTrue(mgr.ProxyServerStatuses.All(status => !status.IsActive));
    }
}
