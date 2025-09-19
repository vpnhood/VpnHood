using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Client.ProxyNodes;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyNodeManagerTest : TestBase
{
    [TestMethod]
    public void IsEnabled_False_When_No_Servers()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyNodeManager(new ProxyOptions(), 
            storagePath: TestHelper.WorkingPath, socketFactory: socketFactory);
        Assert.IsFalse(mgr.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_True_When_Servers_Exist()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyNodeManager(
            proxyOptions: new ProxyOptions {
                ProxyNodes = [new ProxyNode { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 }]
            },
            storagePath: TestHelper.WorkingPath,
            socketFactory: socketFactory);
        Assert.IsTrue(mgr.IsEnabled);
    }

    [TestMethod]
    public async Task ConnectAsync_With_No_Servers_Throws_NetworkUnreachable()
    {
        var socketFactory = new TestSocketFactory();
        var mgr = new ProxyNodeManager(new ProxyOptions(),
            storagePath: TestHelper.WorkingPath, socketFactory: socketFactory);
        var target = new IPEndPoint(IPAddress.Loopback, 443);

        var ex = await Assert.ThrowsExactlyAsync<SocketException>(
            async () => await mgr.ConnectAsync(target, CancellationToken.None));
        Assert.AreEqual((int)SocketError.NetworkUnreachable, ex.ErrorCode);
    }

    [TestMethod]
    public void Constructor_Supports_Multiple_Proxy_Types()
    {
        var socketFactory = new TestSocketFactory();
        var proxyNodes = new[]
        {
            new ProxyNode { Protocol = ProxyProtocol.Socks5, Host = "127.0.0.1", Port = 1080 },
            new ProxyNode { Protocol = ProxyProtocol.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyNode { Protocol = ProxyProtocol.Http, Host = "127.0.0.1", Port = 8080 },
            new ProxyNode { Protocol = ProxyProtocol.Https, Host = "127.0.0.1", Port = 8443 }
        };

        var proxyOptions = new ProxyOptions { ProxyNodes = proxyNodes };
        var mgr = new ProxyNodeManager(proxyOptions: proxyOptions, storagePath: TestHelper.WorkingPath, 
            socketFactory: socketFactory);

        Assert.IsTrue(mgr.IsEnabled);
        Assert.AreEqual(4, mgr.ProxyNodeInfos.Length);
    }

    [TestMethod]
    public async Task RemoveBadServers_Marks_Unsupported_Types_As_Inactive()
    {
        var socketFactory = new TestSocketFactory();
        var proxyNodes = new[]
        {
            new ProxyNode { Protocol = ProxyProtocol.Socks4, Host = "127.0.0.1", Port = 1081 },
            new ProxyNode { Protocol = ProxyProtocol.Http, Host = "127.0.0.1", Port = 8080 }
        };

        var proxyOptions = new ProxyOptions { ProxyNodes = proxyNodes };
        var mgr = new ProxyNodeManager(proxyOptions: proxyOptions, storagePath: TestHelper.WorkingPath,
            socketFactory: socketFactory);
        await mgr.RemoveBadServers(CancellationToken.None);

        // All non-SOCKS5 servers should be marked as inactive
        Assert.IsTrue(mgr.ProxyNodeInfos.All(status => !status.Node.IsEnabled));
    }
}
