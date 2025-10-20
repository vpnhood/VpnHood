using System.Net;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Test.Providers;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Proxies.HttpProxyServers;
using VpnHood.Core.Proxies.Socks5ProxyServers;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ProxyNodeServiceTest : TestAppBase
{

    [TestMethod]
    public async Task List()
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
            Mode = AppProxyMode.Custom
        };
        dom.App.Services.ProxyNodeService.Add(new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
            Port = httpProxyServer.ListenerEndPoint.Port
        });

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);

        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
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
            Mode = AppProxyMode.Custom
        };
        dom.App.Services.ProxyNodeService.Add(new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
            Port = httpProxyServer.ListenerEndPoint.Port
        });

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await TestHelper.Test_Https();

        // make sure new status is fetched from core
        await dom.App.ForceUpdateState();
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.IsGreaterThan(0, nodeInfos[0].Status.SucceededCount);
        var lastSucceededCount = nodeInfos[0].Status.SucceededCount;

        // disconnect 
        await dom.App.Disconnect();
        await dom.App.ForceUpdateState();
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.IsGreaterThanOrEqualTo(lastSucceededCount, nodeInfos[0].Status.SucceededCount);
        lastSucceededCount = nodeInfos[0].Status.SucceededCount;

        // reconnect and make sure status is restored
        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await dom.App.ForceUpdateState();
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
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
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), nodeInfos[0].Node.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, nodeInfos[0].Node.Port);
        Assert.AreEqual(0, nodeInfos[0].Status.SucceededCount);
    }

    [TestMethod]
    public async Task Update_single_proxy_node()
    {
        // add 10 random nodes
        var nodes = new List<ProxyNode>();
        for (var i = 0; i < 10; i++) {
            nodes.Add(new ProxyNode {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };
        foreach (var proxyNode in nodes)
            dom.App.Services.ProxyNodeService.Add(proxyNode);

        // update node[2]
        var newNode = new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{1000}.example.com",
            Port = 1080
        };
        dom.App.Services.ProxyNodeService.Update(nodes[2].Id, newNode);
        var updatedNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(10, updatedNodes);
        Assert.AreEqual(updatedNodes[2].Node.Url, newNode.Url);
    }

    [TestMethod]
    public async Task Duplicate_proxy_should_be_removed()
    {
        // add 10 random nodes
        var nodes = new List<ProxyNode>();
        for (var i = 0; i < 10; i++) {
            nodes.Add(new ProxyNode {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        // add a duplicate of the first node
        nodes.Add(nodes[0]);

        // create the client
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom,
        };
        foreach (var proxyNode in nodes)
            dom.App.Services.ProxyNodeService.Add(proxyNode);

        // check that duplicate is removed
        var updatedNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(10, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(n => n.Node.Id == nodes[0].Id));
    }

    [TestMethod]
    public async Task CRUD_single()
    {
        var nodes = new List<ProxyNode>();
        for (var i = 0; i < 10; i++) {
            nodes.Add(new ProxyNode {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };
        foreach (var proxyNode in nodes)
            dom.App.Services.ProxyNodeService.Add(proxyNode);

        // add a new node
        var newNode = new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{1000}.example.com",
            Port = 1080
        };
        dom.App.Services.ProxyNodeService.Add(newNode);
        var updatedNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(x => x.Node.Id == newNode.Id).ToArray());

        // add same but should be duplicated
        dom.App.Services.ProxyNodeService.Add(newNode);
        updatedNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(x => x.Node.Id == newNode.Id).ToArray());

        // update node[2]
        newNode = new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{2000}.example.com",
            Port = 2080
        };
        dom.App.Services.ProxyNodeService.Update(nodes[2].Id, newNode);
        updatedNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.AreEqual(updatedNodes[2].Node.Url, newNode.Url);

        // check infos
        var updatedAppNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(11, updatedAppNodes);
        Assert.AreEqual(updatedAppNodes[2].Node.Url, newNode.Url);
        Assert.HasCount(1, updatedAppNodes.Where(x => x.Node.Id == newNode.Id));

        // delete node[5]
        dom.App.Services.ProxyNodeService.Delete(nodes[5].Id);
        updatedAppNodes = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(10, updatedAppNodes);
        Assert.HasCount(0, updatedAppNodes.Where(x => x.Node.Id == nodes[5].Id));

    }

    [TestMethod]
    public Task GetNodeInfos()
    {
        // dummy test as vs2022 keep showing this test in test view
        // will clear later
        return Task.CompletedTask;
    }


    [TestMethod]
    public async Task Get_device_proxy()
    {
        using var dom = await AppClientServerDom.CreateWithNullCapture(TestAppHelper);
        var deviceUiProvider = (TestDeviceUiProvider)dom.App.Services.DeviceUiProvider;
        deviceUiProvider.DeviceProxySettings = new DeviceProxySettings {
            ProxyUrl = new Uri("http://foo.local"),
        };

        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.Disabled;
        Assert.IsFalse(dom.App.State.IsProxyNodeActive);

        var deviceProxy = dom.App.Services.ProxyNodeService.GetDeviceProxy();
        Assert.IsNotNull(deviceProxy);
        Assert.AreEqual(deviceUiProvider.DeviceProxySettings.ProxyUrl.Host, deviceProxy.Node.Url.Host);
        Assert.IsFalse(dom.App.State.IsProxyNodeActive);

        // set proxy options to use device proxy
        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.Device;
        Assert.IsTrue(dom.App.State.IsProxyNodeActive);
        Assert.HasCount(1, dom.App.Services.ProxyNodeService.GetProxyOptions().ProxyNodes);
        Assert.AreEqual(deviceProxy.Node.Id, dom.App.Services.ProxyNodeService.GetProxyOptions().ProxyNodes.First().Id);

        // disable proxy
        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.Disabled;
        Assert.IsFalse(dom.App.State.IsProxyNodeActive);
        Assert.HasCount(0, dom.App.Services.ProxyNodeService.GetProxyOptions().ProxyNodes);
    }

    [TestMethod]
    public async Task Connect()
    {
        // create a local SOCKS5 proxy using Socks5ProxyServer
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();

        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);

        // add proxy
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };
        var proxyNode = new ProxyNode {
            Port = socks5ProxyServer.ListenerEndPoint.Port,
            Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
            Protocol = ProxyProtocol.Socks5
        };
        dom.App.Services.ProxyNodeService.Add(proxyNode);

        // connect
        await dom.App.Connect();
        await TestHelper.Test_Https();

        // get info
        await dom.App.ForceUpdateState();
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.IsGreaterThan(0, nodeInfos[0].Status.SucceededCount);
    }

    [TestMethod]
    public async Task Expect_UnreachableProxyException()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);

        // add proxy
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };
        var proxyNode = new ProxyNode {
            Port = 900,
            Host = "localhost",
            Protocol = ProxyProtocol.Socks5
        };
        dom.App.Services.ProxyNodeService.Add(proxyNode);

        // connect
        await Assert.ThrowsAsync<UnreachableProxyServerException>(()=> dom.App.Connect());

        // get info
        await dom.App.ForceUpdateState();
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.AreEqual(0, nodeInfos[0].Status.SucceededCount);
        Assert.IsGreaterThan(0, nodeInfos[0].Status.FailedCount);
    }

    [TestMethod]
    public async Task DeleteAll()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };

        // add 10 proxy nodes
        var nodes = new List<ProxyNode>();
        for (var i = 0; i < 10; i++) {
            nodes.Add(new ProxyNode {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        foreach (var proxyNode in nodes)
            dom.App.Services.ProxyNodeService.Add(proxyNode);

        // verify nodes are added
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(10, nodeInfos);

        // delete all nodes
        dom.App.Services.ProxyNodeService.DeleteAll();

        // verify all nodes are deleted
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(0, nodeInfos);

        // verify proxy is not active after deletion
        Assert.IsFalse(dom.App.State.IsProxyNodeActive);
    }

    [TestMethod]
    public async Task Import_single_proxy()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };

        // import a single proxy
        var proxyText = "socks5://proxy.example.com:1080";
        dom.App.Services.ProxyNodeService.Import(proxyText);

        // verify proxy is imported
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual("proxy.example.com", nodeInfos[0].Node.Host);
        Assert.AreEqual(1080, nodeInfos[0].Node.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, nodeInfos[0].Node.Protocol);
    }

    [TestMethod]
    public async Task Import_multiple_proxies()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };

        // import multiple proxies with various formats
        var proxyText = @"
            socks5://proxy1.example.com:1080
            http://proxy2.example.com:8080
            socks5://user:pass@proxy3.example.com:1081
            http://proxy4.example.com:3128";
        dom.App.Services.ProxyNodeService.Import(proxyText);

        // verify all proxies are imported
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(4, nodeInfos);

        // verify first proxy
        var proxy1 = nodeInfos.First(n => n.Node.Host == "proxy1.example.com");
        Assert.IsNotNull(proxy1);
        Assert.AreEqual(1080, proxy1.Node.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, proxy1.Node.Protocol);

        // verify second proxy
        var proxy2 = nodeInfos.First(n => n.Node.Host == "proxy2.example.com");
        Assert.IsNotNull(proxy2);
        Assert.AreEqual(8080, proxy2.Node.Port);
        Assert.AreEqual(ProxyProtocol.Http, proxy2.Node.Protocol);

        // verify third proxy with authentication
        var proxy3 = nodeInfos.First(n => n.Node.Host == "proxy3.example.com");
        Assert.IsNotNull(proxy3);
        Assert.AreEqual(1081, proxy3.Node.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, proxy3.Node.Protocol);
        Assert.AreEqual("user", proxy3.Node.Username);
        Assert.AreEqual("pass", proxy3.Node.Password);

        // verify fourth proxy
        var proxy4 = nodeInfos.First(n => n.Node.Host == "proxy4.example.com");
        Assert.IsNotNull(proxy4);
        Assert.AreEqual(3128, proxy4.Node.Port);
        Assert.AreEqual(ProxyProtocol.Http, proxy4.Node.Protocol);
    }

    [TestMethod]
    public async Task Import_with_existing_proxies()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };

        // add some existing proxies
        dom.App.Services.ProxyNodeService.Add(new ProxyNode {
            Protocol = ProxyProtocol.Http,
            Host = "existing.example.com",
            Port = 8080
        });

        // verify existing proxy
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);

        // import new proxies
        var proxyText = @"
socks5://proxy1.example.com:1080
http://proxy2.example.com:8080
";
        dom.App.Services.ProxyNodeService.Import(proxyText);

        // verify both existing and imported proxies
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(3, nodeInfos);
        Assert.HasCount(1, nodeInfos.Where(n => n.Node.Host == "existing.example.com"));
        Assert.HasCount(1, nodeInfos.Where(n => n.Node.Host == "proxy1.example.com"));
        Assert.HasCount(1, nodeInfos.Where(n => n.Node.Host == "proxy2.example.com"));
    }

    [TestMethod]
    public async Task Import_duplicate_proxies_should_not_duplicate()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Custom
        };

        // import proxies
        var proxyText = "socks5://proxy.example.com:1080";
        dom.App.Services.ProxyNodeService.Import(proxyText);

        // verify proxy is imported
        var nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        var firstNodeId = nodeInfos[0].Node.Id;

        // import the same proxy again
        dom.App.Services.ProxyNodeService.Import(proxyText);

        // verify no duplicate is created
        nodeInfos = dom.App.Services.ProxyNodeService.ListProxies();
        Assert.HasCount(1, nodeInfos);
        Assert.AreEqual(firstNodeId, nodeInfos[0].Node.Id);
    }
}