using System.Net;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Test.Providers;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Proxies.HttpProxyServers;
using VpnHood.Core.Proxies.Socks5ProxyServers;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ProxyEndPointServiceTest : TestAppBase
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
            Mode = AppProxyMode.Manual
        };
        dom.App.Services.ProxyEndPointService.Add(new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
            Port = httpProxyServer.ListenerEndPoint.Port
        });

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);

        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, endPointInfos[0].EndPoint.Port);
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
            Mode = AppProxyMode.Manual
        };
        dom.App.Services.ProxyEndPointService.Add(new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = httpProxyServer.ListenerEndPoint.Address.ToString(),
            Port = httpProxyServer.ListenerEndPoint.Port
        });

        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await TestHelper.Test_Https();

        // make sure new status is fetched from core
        await dom.App.ForceUpdateState();
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, endPointInfos[0].EndPoint.Port);
        Assert.IsGreaterThan(0, endPointInfos[0].Status.SucceededCount);
        var lastSucceededCount = endPointInfos[0].Status.SucceededCount;

        // disconnect 
        await dom.App.Disconnect();
        await dom.App.ForceUpdateState();
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, endPointInfos[0].EndPoint.Port);
        Assert.IsGreaterThanOrEqualTo(lastSucceededCount, endPointInfos[0].Status.SucceededCount);
        lastSucceededCount = endPointInfos[0].Status.SucceededCount;

        // reconnect and make sure status is restored
        await dom.App.Connect();
        await dom.App.WaitForState(AppConnectionState.Connected);
        await dom.App.ForceUpdateState();
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, endPointInfos[0].EndPoint.Port);
        Assert.IsGreaterThanOrEqualTo(lastSucceededCount, endPointInfos[0].Status.SucceededCount);

        // use more connection
        await TestAppHelper.Test_Https();
        await TestAppHelper.Test_Https();
        await dom.App.ForceUpdateState();

        // clear status
        dom.App.Services.ProxyEndPointService.ResetStates();
        await dom.App.ForceUpdateState();
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Address.ToString(), endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(httpProxyServer.ListenerEndPoint.Port, endPointInfos[0].EndPoint.Port);
        Assert.AreEqual(0, endPointInfos[0].Status.SucceededCount);
    }

    [TestMethod]
    public async Task Update_single_proxy_endpoint()
    {
        // add 10 random endpoints
        var endpoints = new List<ProxyEndPoint>();
        for (var i = 0; i < 10; i++) {
            endpoints.Add(new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };
        foreach (var proxyEndPoint in endpoints)
            dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // update endpoint[2]
        var newNode = new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{1000}.example.com",
            Port = 1080
        };
        dom.App.Services.ProxyEndPointService.Update(endpoints[2].Id, newNode);
        var updatedNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(10, updatedNodes);
        Assert.AreEqual(updatedNodes[2].EndPoint.Url, newNode.Url);
    }

    [TestMethod]
    public async Task Duplicate_proxy_should_be_removed()
    {
        // add 10 random endpoints
        var endpoints = new List<ProxyEndPoint>();
        for (var i = 0; i < 10; i++) {
            endpoints.Add(new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        // add a duplicate of the first endpoint
        endpoints.Add(endpoints[0]);

        // create the client
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };
        foreach (var proxyEndPoint in endpoints)
            dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // check that duplicate is removed
        var updatedNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(10, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(n => n.EndPoint.Id == endpoints[0].Id));
    }

    [TestMethod]
    public async Task CRUD_single()
    {
        var endpoints = new List<ProxyEndPoint>();
        for (var i = 0; i < 10; i++) {
            endpoints.Add(new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };
        foreach (var proxyEndPoint in endpoints)
            dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // Get
        var proxyEndPointInfo = dom.App.Services.ProxyEndPointService.Get(endpoints.First().Id);
        Assert.AreEqual(endpoints.First().Id, proxyEndPointInfo.EndPoint.Id);
        Assert.AreEqual(endpoints.First().Host, proxyEndPointInfo.EndPoint.Host);

        // add a new endpoint
        var newNode = new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{1000}.example.com",
            Port = 1080
        };
        dom.App.Services.ProxyEndPointService.Add(newNode);
        var updatedNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(x => x.EndPoint.Id == newNode.Id).ToArray());

        // add same but should be duplicated
        dom.App.Services.ProxyEndPointService.Add(newNode);
        updatedNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.HasCount(1, updatedNodes.Where(x => x.EndPoint.Id == newNode.Id).ToArray());

        // update endpoint[2]
        newNode = new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = $"proxy{2000}.example.com",
            Port = 2080
        };
        dom.App.Services.ProxyEndPointService.Update(endpoints[2].Id, newNode);
        updatedNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(11, updatedNodes);
        Assert.AreEqual(updatedNodes[2].EndPoint.Url, newNode.Url);

        // check infos
        var updatedAppNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(11, updatedAppNodes);
        Assert.AreEqual(updatedAppNodes[2].EndPoint.Url, newNode.Url);
        Assert.HasCount(1, updatedAppNodes.Where(x => x.EndPoint.Id == newNode.Id));

        // delete endpoint[5]
        dom.App.Services.ProxyEndPointService.Delete(endpoints[5].Id);
        updatedAppNodes = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(10, updatedAppNodes);
        Assert.HasCount(0, updatedAppNodes.Where(x => x.EndPoint.Id == endpoints[5].Id));
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
            ProxyUrl = new Uri("http://foo.local")
        };

        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.NoProxy;
        Assert.IsFalse(dom.App.State.IsProxyEndPointActive);

        var deviceProxy = dom.App.Services.ProxyEndPointService.GetDeviceProxy();
        Assert.IsNotNull(deviceProxy);
        Assert.AreEqual(deviceUiProvider.DeviceProxySettings.ProxyUrl.Host, deviceProxy.EndPoint.Url.Host);
        Assert.IsFalse(dom.App.State.IsProxyEndPointActive);

        // set proxy options to use device proxy
        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.Device;
        Assert.IsTrue(dom.App.State.IsProxyEndPointActive);
        Assert.HasCount(1, dom.App.Services.ProxyEndPointService.GetProxyOptions().ProxyEndPoints);
        Assert.AreEqual(deviceProxy.EndPoint.Id,
            dom.App.Services.ProxyEndPointService.GetProxyOptions().ProxyEndPoints.First().Id);

        // disable proxy
        dom.App.UserSettings.ProxySettings.Mode = AppProxyMode.NoProxy;
        Assert.IsFalse(dom.App.State.IsProxyEndPointActive);
        Assert.HasCount(0, dom.App.Services.ProxyEndPointService.GetProxyOptions().ProxyEndPoints);
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
            Mode = AppProxyMode.Manual
        };
        var proxyEndPoint = new ProxyEndPoint {
            Port = socks5ProxyServer.ListenerEndPoint.Port,
            Host = socks5ProxyServer.ListenerEndPoint.Address.ToString(),
            Protocol = ProxyProtocol.Socks5
        };
        dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // connect
        await dom.App.Connect();
        await TestHelper.Test_Https();

        // get info
        await dom.App.ForceUpdateState();
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.IsGreaterThan(0, endPointInfos[0].Status.SucceededCount);
    }

    [TestMethod]
    public async Task Expect_UnreachableProxyException()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);

        // add proxy
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };
        var proxyEndPoint = new ProxyEndPoint {
            Port = 900,
            Host = "localhost",
            Protocol = ProxyProtocol.Socks5
        };
        dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // connect
        await Assert.ThrowsAsync<UnreachableProxyServerException>(() => dom.App.Connect());

        // get info
        await dom.App.ForceUpdateState();
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.AreEqual(0, endPointInfos[0].Status.SucceededCount);
        Assert.IsGreaterThan(0, endPointInfos[0].Status.FailedCount);
    }

    [TestMethod]
    public async Task DeleteAll()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };

        // add 10 proxy endpoints
        var endpoints = new List<ProxyEndPoint>();
        for (var i = 0; i < 10; i++) {
            endpoints.Add(new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks5,
                Host = $"proxy{i}.example.com",
                Port = 1080 + i
            });
        }

        foreach (var proxyEndPoint in endpoints)
            dom.App.Services.ProxyEndPointService.Add(proxyEndPoint);

        // verify endpoints are added
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(10, endPointInfos);

        // delete all endpoints
        dom.App.Services.ProxyEndPointService.DeleteAll();

        // verify all endpoints are deleted
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(0, endPointInfos);

        // verify proxy is not active after deletion
        Assert.IsFalse(dom.App.State.IsProxyEndPointActive);
    }

    [TestMethod]
    public async Task Import_single_proxy()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };

        // import a single proxy
        var proxyText = "socks5://proxy.example.com:1080";
        dom.App.Services.ProxyEndPointService.Import(proxyText);

        // verify proxy is imported
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual("proxy.example.com", endPointInfos[0].EndPoint.Host);
        Assert.AreEqual(1080, endPointInfos[0].EndPoint.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, endPointInfos[0].EndPoint.Protocol);
    }

    [TestMethod]
    public async Task Import_multiple_proxies()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };

        // import multiple proxies with various formats
        var proxyText = @"
            socks5://proxy1.example.com:1080
            http://proxy2.example.com:8080
            socks5://user:pass@proxy3.example.com:1081
            http://proxy4.example.com:3128";
        dom.App.Services.ProxyEndPointService.Import(proxyText);

        // verify all proxies are imported
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(4, endPointInfos);

        // verify first proxy
        var proxy1 = endPointInfos.First(n => n.EndPoint.Host == "proxy1.example.com");
        Assert.IsNotNull(proxy1);
        Assert.AreEqual(1080, proxy1.EndPoint.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, proxy1.EndPoint.Protocol);

        // verify second proxy
        var proxy2 = endPointInfos.First(n => n.EndPoint.Host == "proxy2.example.com");
        Assert.IsNotNull(proxy2);
        Assert.AreEqual(8080, proxy2.EndPoint.Port);
        Assert.AreEqual(ProxyProtocol.Http, proxy2.EndPoint.Protocol);

        // verify third proxy with authentication
        var proxy3 = endPointInfos.First(n => n.EndPoint.Host == "proxy3.example.com");
        Assert.IsNotNull(proxy3);
        Assert.AreEqual(1081, proxy3.EndPoint.Port);
        Assert.AreEqual(ProxyProtocol.Socks5, proxy3.EndPoint.Protocol);
        Assert.AreEqual("user", proxy3.EndPoint.Username);
        Assert.AreEqual("pass", proxy3.EndPoint.Password);

        // verify fourth proxy
        var proxy4 = endPointInfos.First(n => n.EndPoint.Host == "proxy4.example.com");
        Assert.IsNotNull(proxy4);
        Assert.AreEqual(3128, proxy4.EndPoint.Port);
        Assert.AreEqual(ProxyProtocol.Http, proxy4.EndPoint.Protocol);
    }

    [TestMethod]
    public async Task Import_with_existing_proxies()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };

        // add some existing proxies
        dom.App.Services.ProxyEndPointService.Add(new ProxyEndPoint {
            Protocol = ProxyProtocol.Http,
            Host = "existing.example.com",
            Port = 8080
        });

        // verify existing proxy
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);

        // import new proxies
        var proxyText = @"
            socks5://proxy1.example.com:1080
            http://proxy2.example.com:8080
         ";
        dom.App.Services.ProxyEndPointService.Import(proxyText);

        // verify both existing and imported proxies
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(3, endPointInfos);
        Assert.HasCount(1, endPointInfos.Where(n => n.EndPoint.Host == "existing.example.com"));
        Assert.HasCount(1, endPointInfos.Where(n => n.EndPoint.Host == "proxy1.example.com"));
        Assert.HasCount(1, endPointInfos.Where(n => n.EndPoint.Host == "proxy2.example.com"));
    }

    [TestMethod]
    public async Task Import_duplicate_proxies_should_not_duplicate()
    {
        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual
        };

        // import proxies
        var proxyText = "socks5://proxy.example.com:1080";
        dom.App.Services.ProxyEndPointService.Import(proxyText);

        // verify proxy is imported
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        var firstNodeId = endPointInfos[0].EndPoint.Id;

        // import the same proxy again
        dom.App.Services.ProxyEndPointService.Import(proxyText);

        // verify no duplicate is created
        endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(1, endPointInfos);
        Assert.AreEqual(firstNodeId, endPointInfos[0].EndPoint.Id);
    }

    [TestMethod]
    public async Task Update_by_remote_url_core()
    {
        // run two socks5 and http proxy servers locally
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();
        var socks5Ep = socks5ProxyServer.ListenerEndPoint;

        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();
        var httpEp = httpProxyServer.ListenerEndPoint;

        // add our proxy endpoints to a remote file content
        var proxyListContent =
            $"socks5://{socks5Ep}\r\n" +
            $"http://{httpEp}\r\n";
        TestAppHelper.WebServer.FileContent1 = proxyListContent;

        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual,
            AutoUpdateOptions = new ProxyAutoUpdateOptions {
                Url = dom.TestAppHelper.WebServer.FileHttpUrl1,
                Interval = TimeSpan.FromMinutes(1)
            }
        };

        // connect 
        await dom.App.Connect();

        // force sync with core
        await dom.App.ForceUpdateState();

        // check is proxies are added
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(2, endPointInfos);
        Assert.HasCount(1,
            endPointInfos.Where(n => n.EndPoint.Protocol == ProxyProtocol.Socks5 && n.EndPoint.Port == socks5Ep.Port));
        Assert.HasCount(1,
            endPointInfos.Where(n => n.EndPoint.Protocol == ProxyProtocol.Http && n.EndPoint.Port == httpEp.Port));
    }

    [TestMethod]
    public async Task Update_by_remote_url_instance()
    {
        // run two socks5 and http proxy servers locally
        using var socks5ProxyServer = new Socks5ProxyServer(new Socks5ProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        socks5ProxyServer.Start();
        var socks5Ep = socks5ProxyServer.ListenerEndPoint;

        using var httpProxyServer = new HttpProxyServer(new HttpProxyServerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        httpProxyServer.Start();
        var httpEp = httpProxyServer.ListenerEndPoint;

        // add our proxy endpoints to a remote file content
        var proxyListContent =
            $"socks5://{socks5Ep}\r\n" +
            $"http://{httpEp}\r\n";
        TestAppHelper.WebServer.FileContent1 = proxyListContent;

        // create app
        using var dom = await AppClientServerDom.Create(TestAppHelper);
        dom.App.UserSettings.ProxySettings = new AppProxySettings {
            Mode = AppProxyMode.Manual,
            AutoUpdateOptions = new ProxyAutoUpdateOptions {
                Url = dom.TestAppHelper.WebServer.FileHttpUrl1,
                Interval = TimeSpan.FromMinutes(1)
            }
        };

        // check is proxies are added
        await dom.App.Services.ProxyEndPointService.ReloadUrl(CancellationToken.None);
        var endPointInfos = dom.App.Services.ProxyEndPointService.ListProxies();
        Assert.HasCount(2, endPointInfos);
        Assert.HasCount(1,
            endPointInfos.Where(n => n.EndPoint.Protocol == ProxyProtocol.Socks5 && n.EndPoint.Port == socks5Ep.Port));
        Assert.HasCount(1,
            endPointInfos.Where(n => n.EndPoint.Protocol == ProxyProtocol.Http && n.EndPoint.Port == httpEp.Port));
    }
}