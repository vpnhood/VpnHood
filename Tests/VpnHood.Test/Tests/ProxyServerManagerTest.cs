using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ClientProxies;
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
            proxyServerEndPoints: [new ProxyServerEndPoint { ProxyServerType = ProxyServerType.Socks5, Address = "127.0.0.1", Port = 1080 }],
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
}
