using System.Net;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Proxies.HttpProxyServers;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ProxyNodeServiceTest : TestAppBase
{

    [TestMethod]
    public async Task Connect_via_proxy()
    {
        // create a local HTTP proxy using HttpProxyServer
        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();

        // create server
        using var dom = await AppClientServerDom.Create(TestAppHelper);

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

        var nodeInfos = await dom.App.Services.ProxyNodeService.GetNodeInfos(CancellationToken.None);
        Assert.AreEqual(1, nodeInfos.Length);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
    }
}