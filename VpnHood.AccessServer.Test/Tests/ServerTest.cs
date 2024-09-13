using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        using var farm = await ServerFarmDom.Create();
        var oldConfigCode = farm.DefaultServer.ServerConfig.ConfigCode;
        await farm.DefaultServer.Client.ReconfigureAsync(farm.ProjectId, farm.DefaultServer.ServerId);

        await farm.DefaultServer.Configure();
        Assert.AreNotEqual(oldConfigCode, farm.DefaultServer.ServerConfig.ConfigCode);
    }

    [TestMethod]
    public async Task List()
    {
        var testApp = await TestApp.Create();
        var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        var server1 = await farm.AddNewServer();
        await farm.AddNewServer();

        // list by ip address
        var servers = await server1.Client.ListAsync(farm.ProjectId, ipAddress: server1.ServerConfig.TcpEndPoints!.First().Address.ToString());
        Assert.AreEqual(1, servers.Count);
        Assert.AreEqual(server1.ServerId, servers.Single().Server.ServerId);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testApp = await TestApp.Create();
        var farm = await ServerFarmDom.Create(testApp, serverCount: 0);

        //-----------
        // check: Create
        //-----------
        var server1ACreateParam = new ServerCreateParams {
            ServerName = $"{Guid.NewGuid()}",
            HostPanelUrl = new Uri("http://localhost/foo"),
            Power = 8,
        };
        var serverDom = await farm.AddNewServer(server1ACreateParam, configure: false);
        var install1A = await farm.TestApp.ServersClient.GetInstallManualAsync(testApp.ProjectId, serverDom.ServerId);

        //-----------
        // check: Get
        //-----------
        await serverDom.Reload();
        Assert.AreEqual(server1ACreateParam.HostPanelUrl, serverDom.Server.HostPanelUrl);
        Assert.AreEqual(server1ACreateParam.ServerName, serverDom.Server.ServerName);
        Assert.AreEqual(server1ACreateParam.Power, serverDom.Server.Power);
        Assert.AreEqual(ServerState.NotInstalled, serverDom.Server.ServerState);

        // ServerState.Configuring
        serverDom.ServerInfo = testApp.NewServerInfo(randomStatus: true, publicIpV4: IPAddress.Parse("5.0.0.1"));
        serverDom.ServerInfo.Status.SessionCount = 0;
        await serverDom.Configure(false);
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);

        // ServerState.Idle
        serverDom.ServerInfo.Status.SessionCount = 0;
        await serverDom.SendStatus();
        await serverDom.Reload();
        Assert.AreEqual("5", serverDom.Server.Location?.CountryCode);
        Assert.AreEqual(ServerState.Idle, serverDom.Server.ServerState);

        // ServerState.Active
        serverDom.ServerInfo.Status = TestApp.NewServerStatus(serverDom.ServerConfig.ConfigCode, true);
        await serverDom.SendStatus();
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Active, serverDom.Server.ServerState);

        // ServerState.Configuring
        await serverDom.Client.ReconfigureAsync(testApp.ProjectId, serverDom.ServerId);
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);

        //-----------
        // check: Update (Don't change Secret)
        //-----------
        var serverUpdateParam = new ServerUpdateParams {
            ServerName = new PatchOfString { Value = $"{Guid.NewGuid()}" },
            AutoConfigure = new PatchOfBoolean { Value = !serverDom.Server.AutoConfigure },
            GenerateNewSecret = new PatchOfBoolean { Value = false },
            HostPanelUrl = new PatchOfUri { Value = new Uri("http://localhost/foo2") },
            Power = new PatchOfNullableInteger { Value = 16 },
            IsEnabled = new PatchOfBoolean { Value = !serverDom.Server.IsEnabled }
        };
        await serverDom.Update(serverUpdateParam);
        await serverDom.Reload();
        var install1C = await serverDom.Client.GetInstallManualAsync(testApp.ProjectId, serverDom.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.ManagementSecret, install1C.AppSettings.ManagementSecret);
        Assert.AreEqual(serverUpdateParam.AutoConfigure.Value, serverDom.Server.AutoConfigure);
        Assert.AreEqual(serverUpdateParam.HostPanelUrl.Value, serverDom.Server.HostPanelUrl);
        Assert.AreEqual(serverUpdateParam.ServerName.Value, serverDom.Server.ServerName);
        Assert.AreEqual(serverUpdateParam.IsEnabled.Value, serverDom.Server.IsEnabled);

        //-----------
        // check: Update (change Secret)
        //-----------
        serverUpdateParam = new ServerUpdateParams { GenerateNewSecret = new PatchOfBoolean { Value = true } };
        await serverDom.Update(serverUpdateParam);
        install1C = await serverDom.Client.GetInstallManualAsync(testApp.ProjectId, serverDom.Server.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.ManagementSecret, install1C.AppSettings.ManagementSecret);

        //-----------
        // check: Update (serverFarmId)
        //-----------
        var farm2 = await ServerFarmDom.Create(farm.TestApp);
        serverUpdateParam = new ServerUpdateParams { ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId } };
        await serverDom.Client.UpdateAsync(testApp.ProjectId, serverDom.ServerId, serverUpdateParam);
        await serverDom.Reload();
        Assert.AreEqual(farm2.ServerFarmId, serverDom.Server.ServerFarmId);

        //-----------
        // check: CertificateList
        //-----------
        var servers = await serverDom.Client.ListAsync(testApp.ProjectId);
        Assert.IsTrue(servers.Any(x =>
            x.Server.ServerName == serverDom.Server.ServerName && x.Server.ServerId == serverDom.ServerId));

        //-----------
        // check: Delete
        //-----------
        await serverDom.Client.DeleteAsync(testApp.ProjectId, serverDom.ServerId);
        try {
            await serverDom.Reload();
            Assert.Fail($"{nameof(NotExistsException)} was expected");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Quota()
    {
        using var farm = await ServerFarmDom.Create();
        QuotaConstants.ServerCount = farm.Servers.Count; //update quota

        try {
            await farm.AddNewServer();
            Assert.Fail($"{nameof(QuotaException)} is expected");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task GetInstallManual()
    {
        using var farm = await ServerFarmDom.Create();
        var install =
            await farm.DefaultServer.Client.GetInstallManualAsync(farm.ProjectId, farm.DefaultServer.ServerId);

        var actualAppSettings = JsonSerializer.Deserialize<ServerInstallAppSettings>(install.AppSettingsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.AreEqual(Convert.ToBase64String(install.AppSettings.ManagementSecret),
            Convert.ToBase64String(actualAppSettings.ManagementSecret));
        Assert.AreEqual(install.AppSettings.HttpAccessManager.BaseUrl, actualAppSettings.HttpAccessManager.BaseUrl);
        Assert.IsTrue(install.LinuxCommand.Contains("x64.sh"));
        Assert.IsTrue(install.WindowsCommand.Contains("x64.ps1"));
    }

    [TestMethod]
    public async Task ServerInstallByUserName()
    {
        using var farm = await ServerFarmDom.Create();
        try {
            await farm.DefaultServer.Client.InstallBySshUserPasswordAsync(farm.ProjectId, farm.DefaultServer.ServerId,
                new ServerInstallBySshUserPasswordParams { HostName = "127.0.0.1", LoginUserName = "user", LoginPassword = "pass" });
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);
        }

        try {
            await farm.DefaultServer.Client.InstallBySshUserKeyAsync(farm.ProjectId, farm.DefaultServer.ServerId,
                new ServerInstallBySshUserKeyParams { HostName = "127.0.0.1", LoginUserName = "user", UserPrivateKey = TestResource.test_ssh_key });
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_should_not_create_for_another_project_farm()
    {
        var p1Farm = await ServerFarmDom.Create();
        var p2Farm = await ServerFarmDom.Create();

        try {
            await p1Farm.TestApp.ServersClient.CreateAsync(p1Farm.ProjectId,
                new ServerCreateParams {
                    ServerName = $"{Guid.NewGuid()}",
                    ServerFarmId = p2Farm.ServerFarmId
                });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_should_not_update_to_another_project_farm()
    {
        var p1Farm = await ServerFarmDom.Create();
        var p2Farm = await ServerFarmDom.Create();

        try {
            await p1Farm.DefaultServer.Update(new ServerUpdateParams {
                ServerFarmId = new PatchOfGuid { Value = p2Farm.ServerFarmId }
            });

            Assert.Fail($"{nameof(NotExistsException)} was expected.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_make_sure_the_deleted_server_AccessPoints_not_exists_in_the_token()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);

        // get first server token
        var server1Dom = await farm.AddNewServer();
        var server1TokenIp =
            server1Dom.Server.AccessPoints.First(x => x.AccessPointMode == AccessPointMode.PublicInToken);

        // check server access point in token
        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.ToString() == server1TokenIp.IpAddress));

        // add another server
        var server2Dom = await farm.AddNewServer(new ServerCreateParams {
            AccessPoints = [farm.TestApp.NewAccessPoint()]
        });
        var server2TokenIp =
            server2Dom.Server.AccessPoints.First(x => x.AccessPointMode == AccessPointMode.PublicInToken);
        accessToken = await farm.CreateAccessToken();
        token = await accessToken.GetToken();

        // both server AccessPoint must exist
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.ToString() == server1TokenIp.IpAddress));
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.ToString() == server2TokenIp.IpAddress));

        // delete server 1 and check that its token should not exist in access-token anymore
        await server1Dom.Delete();
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.ToString() == server1TokenIp.IpAddress));
    }

    [TestMethod]
    public async Task Crud_AccessPoints()
    {
        var testApp = await TestApp.Create();
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);

        var accessPoint1 = testApp.NewAccessPoint();
        var accessPoint2 = testApp.NewAccessPoint();

        // create server
        var serverDom = await farm.AddNewServer(new ServerCreateParams {
            AccessPoints = [accessPoint1, accessPoint2]
        });

        //-----------
        // check: serverFarmId is created
        //-----------
        var accessPoint1B = serverDom.Server.AccessPoints.ToArray()[0];
        var accessPoint2B = serverDom.Server.AccessPoints.ToArray()[1];
        Assert.AreEqual(accessPoint1.IpAddress, accessPoint1B.IpAddress);
        Assert.AreEqual(accessPoint1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(accessPoint1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(accessPoint1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint1.IsListen, accessPoint1B.IsListen); // first group must be default

        await serverDom.Reload();
        Assert.AreEqual(accessPoint1.IpAddress, accessPoint1B.IpAddress);
        Assert.AreEqual(accessPoint1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(accessPoint1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(accessPoint1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint1.IsListen, accessPoint1B.IsListen); // first group must be default

        Assert.AreEqual(accessPoint2.IpAddress, accessPoint2B.IpAddress);
        Assert.AreEqual(accessPoint2.TcpPort, accessPoint2B.TcpPort);
        Assert.AreEqual(accessPoint2.UdpPort, accessPoint2B.UdpPort);
        Assert.AreEqual(accessPoint2.AccessPointMode, accessPoint2B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint2.IsListen, accessPoint2B.IsListen); // first group must be default

        //-----------
        // check: update 
        //-----------
        var oldConfig = (await serverDom.SendStatus(serverDom.ServerInfo.Status)).ConfigCode;
        var accessPoint3 = testApp.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [accessPoint3] }
        });
        var newConfig = (await serverDom.SendStatus(serverDom.ServerInfo.Status)).ConfigCode;
        Assert.AreNotEqual(oldConfig, newConfig);

        await serverDom.Reload();
        Assert.AreEqual(1, serverDom.Server.AccessPoints.ToArray().Length);
        var accessPoint3B = serverDom.Server.AccessPoints.ToArray()[0];
        Assert.AreEqual(accessPoint3.IpAddress, accessPoint3B.IpAddress);
        Assert.AreEqual(accessPoint3.TcpPort, accessPoint3B.TcpPort);
        Assert.AreEqual(accessPoint3.UdpPort, accessPoint3B.UdpPort);
        Assert.AreEqual(accessPoint3.AccessPointMode, accessPoint3B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint3.IsListen, accessPoint3B.IsListen); // first group must be default

        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsTrue(
            token.ServerToken.HostEndPoints!.Any(x =>
                x.Address.ToString() == accessPoint3.IpAddress && x.Port == accessPoint3.TcpPort),
            "AccessPoints have not been updated in FarmToken.");
    }

    [TestMethod]
    public async Task GetStatusSummary()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(1);
        farm.TestApp.AgentTestApp.AgentOptions.LostServerThreshold = TimeSpan.FromSeconds(2);

        // lost
        var sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 10 });
        await Task.Delay(2000);

        // active 2
        sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 1, TunnelSpeed = new Traffic { Received = 100, Sent = 50 } });
        sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 2, TunnelSpeed = new Traffic { Received = 300, Sent = 200 } });

        // disabled 1
        sampleServer = await farm.AddNewServer();
        await sampleServer.Update(new ServerUpdateParams { IsEnabled = new PatchOfBoolean { Value = false } });

        // notInstalled 4
        await farm.AddNewServer(configure: false);
        await farm.AddNewServer(configure: false);
        await farm.AddNewServer(configure: false);
        await farm.AddNewServer(configure: false);

        // idle1
        sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 0 });

        // idle2
        sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 0 });

        // idle3
        sampleServer = await farm.AddNewServer();
        await sampleServer.SendStatus(new ServerStatus { SessionCount = 0 });

        var liveUsageSummary = await farm.TestApp.ServersClient.GetStatusSummaryAsync(farm.TestApp.ProjectId);
        Assert.AreEqual(11, liveUsageSummary.TotalServerCount);
        Assert.AreEqual(1, liveUsageSummary.DisabledServerCount);
        Assert.AreEqual(2, liveUsageSummary.ActiveServerCount);
        Assert.AreEqual(4, liveUsageSummary.NotInstalledServerCount);
        Assert.AreEqual(1, liveUsageSummary.LostServerCount);
        Assert.AreEqual(3, liveUsageSummary.IdleServerCount);
        Assert.AreEqual(250, liveUsageSummary.TunnelSendSpeed);
        Assert.AreEqual(400, liveUsageSummary.TunnelReceiveSpeed);
    }

    [TestMethod]
    public async Task Delete_should_change_farm_token()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var accessToken = await farm.CreateAccessToken();

        // error expected when there is no public in token access point
        await VhTestUtil.AssertApiException("InvalidOperationException", accessToken.GetToken(), contains: "token has not been initialized");

        var serverDom1 = await farm.AddNewServer(new ServerCreateParams {
            AccessPoints = [farm.TestApp.NewAccessPoint(accessPointMode: AccessPointMode.PublicInToken)]
        });

        await farm.AddNewServer(new ServerCreateParams {
            AccessPoints = [farm.TestApp.NewAccessPoint(accessPointMode: AccessPointMode.Public)]
        });

        // work without error
        await farm.Reload();
        Assert.IsNull(farm.ServerFarm.TokenError);

        // first server should generate new farm token
        await serverDom1.Delete();
        await farm.Reload();
        Assert.IsNotNull(farm.ServerFarm.TokenError);
    }
}