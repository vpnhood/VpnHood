using System.Net;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Proxies.HttpProxyServers;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ProxyNodeServiceTest : TestAppBase
{

    [TestMethod]
    public async Task GetNodeInfos()
    {
        // create a local HTTP proxy using HttpProxyServer
        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();

        // create server
        using var dom = await AppClientServerDom.CreateWithNullCapture(TestAppHelper);

        // set proxy settings to use the local HTTP proxy
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom,
            Nodes = [
                new ProxyNode {
                    Protocol = ProxyProtocol.Http,
                    Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = httpProxyServer.ListenerEndPoint.Port
                }
            ]
        };

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);

        var nodeInfos = dom.App.Services.ProxyNodeService.GetNodeInfos();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
    }

    [TestMethod]
    public async Task Restore_last_NodeInfo_then_clear()
    {

        // create a local HTTP proxy using HttpProxyServer
        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();

        // create server
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.DebugData1 += " " + DebugCommands.NoTcpReuse;

        // set proxy settings to use the local HTTP proxy
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom,
            Nodes = [
                new ProxyNode {
                    Protocol = ProxyProtocol.Http,
                    Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
                    Port = httpProxyServer.ListenerEndPoint.Port
                }
            ]
        };

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await TestHelper.Test_Https();

        // make sure new status is fetched from core
        await dom.App.ForceUpdateState();
        var nodeInfos = dom.App.Services.ProxyNodeService.GetNodeInfos();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.IsGreaterThan(0, nodeInfos[0].Status.SucceededCount);
        var lastSucceededCount = nodeInfos[0].Status.SucceededCount;

        // disconnect 
        await dom.App.Disconnect();
        await dom.App.ForceUpdateState();
        nodeInfos = dom.App.Services.ProxyNodeService.GetNodeInfos();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.IsGreaterThanOrEqualTo(lastSucceededCount, nodeInfos[0].Status.SucceededCount);
        lastSucceededCount = nodeInfos[0].Status.SucceededCount;

        // reconnect and make sure status is restored
        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await dom.App.ForceUpdateState();
        nodeInfos = dom.App.Services.ProxyNodeService.GetNodeInfos();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.IsGreaterThanOrEqualTo(lastSucceededCount, nodeInfos[0].Status.SucceededCount);

        // use more connection
        await TestAppHelper.Test_Https();
        await TestAppHelper.Test_Https();
        await dom.App.ForceUpdateState();

        // clear status
        dom.App.Services.ProxyNodeService.ResetStates();
        await dom.App.ForceUpdateState();
        nodeInfos = dom.App.Services.ProxyNodeService.GetNodeInfos();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.AreEqual(0, nodeInfos[0].Status.SucceededCount);
    }
}