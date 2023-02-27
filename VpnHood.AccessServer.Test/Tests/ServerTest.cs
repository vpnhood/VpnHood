using System;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        var farm = await ServerFarmDom.Create();
        var oldConfigCode = farm.DefaultServer.ServerConfig.ConfigCode;
        await farm.DefaultServer.Client.ReconfigureAsync(farm.ProjectId, farm.DefaultServer.ServerId);

        await farm.DefaultServer.Configure();
        Assert.AreNotEqual(oldConfigCode, farm.DefaultServer.ServerConfig.ConfigCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var farm1 = await ServerFarmDom.Create(testInit, serverCount: 0);

        //-----------
        // check: Create
        //-----------
        var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var serverDom = await farm1.AddNewServer(server1ACreateParam, configure: false);
        var install1A = await farm1.TestInit.ServersClient.GetInstallManualAsync(testInit.ProjectId, serverDom.ServerId);

        //-----------
        // check: Get
        //-----------
        await serverDom.Reload();
        Assert.AreEqual(server1ACreateParam.ServerName, serverDom.Server.ServerName);
        Assert.AreEqual(ServerState.NotInstalled, serverDom.Server.ServerState);

        // ServerState.Configuring
        serverDom.ServerInfo = await testInit.NewServerInfo(randomStatus: true);
        serverDom.ServerInfo.Status.SessionCount = 0;
        await serverDom.Configure(false);
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);

        // ServerState.Idle
        serverDom.ServerInfo.Status.SessionCount = 0;
        await serverDom.SendStatus();
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Idle, serverDom.Server.ServerState);

        // ServerState.Active
        serverDom.ServerInfo.Status = TestInit.NewServerStatus(serverDom.ServerConfig.ConfigCode, true);
        await serverDom.SendStatus();
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Active, serverDom.Server.ServerState);

        // ServerState.Configuring
        await serverDom.Client.ReconfigureAsync(testInit.ProjectId, serverDom.ServerId);
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);

        //-----------
        // check: Update (Don't change Secret)
        //-----------
        var serverUpdateParam = new ServerUpdateParams
        {
            ServerName = new PatchOfString { Value = $"{Guid.NewGuid()}" },
            AutoConfigure = new PatchOfBoolean { Value = !serverDom.Server.AutoConfigure },
            GenerateNewSecret = new PatchOfBoolean { Value = false }
        };
        await serverDom.Update(serverUpdateParam);
        await serverDom.Reload();
        var install1C = await serverDom.Client.GetInstallManualAsync(testInit.ProjectId, serverDom.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);
        Assert.AreEqual(serverUpdateParam.AutoConfigure.Value, serverDom.Server.AutoConfigure);
        Assert.AreEqual(serverUpdateParam.ServerName.Value, serverDom.Server.ServerName);

        //-----------
        // check: Update (change Secret)
        //-----------
        serverUpdateParam = new ServerUpdateParams { GenerateNewSecret = new PatchOfBoolean { Value = true } };
        await serverDom.Update(serverUpdateParam);
        install1C = await serverDom.Client.GetInstallManualAsync(testInit.ProjectId, serverDom.Server.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);

        //-----------
        // check: Update (serverFarmId)
        //-----------
        var farm2 = await ServerFarmDom.Create(farm1.TestInit);
        serverUpdateParam = new ServerUpdateParams { ServerFarmId = new PatchOfNullableGuid { Value = farm2.ServerFarmId } };
        await serverDom.Client.UpdateAsync(testInit.ProjectId, serverDom.ServerId, serverUpdateParam);
        await serverDom.Reload();
        Assert.AreEqual(farm2.ServerFarmId, serverDom.Server.ServerFarmId);

        //-----------
        // check: List
        //-----------
        var servers = await serverDom.Client.ListAsync(testInit.ProjectId);
        Assert.IsTrue(servers.Any(x => x.Server.ServerName == serverDom.Server.ServerName && x.Server.ServerId == serverDom.ServerId));

        //-----------
        // check: Delete
        //-----------
        await serverDom.Client.DeleteAsync(testInit.ProjectId, serverDom.ServerId);
        try
        {
            await serverDom.Reload();
            Assert.Fail($"{nameof(NotExistsException)} was expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Quota()
    {
        var farm = await ServerFarmDom.Create();
        QuotaConstants.ServerCount = farm.Servers.Count; //update quota

        try
        {
            await farm.AddNewServer();
            Assert.Fail($"{nameof(QuotaException)} is expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task GetInstallManual()
    {
        var farm = await ServerFarmDom.Create();
        var install = await farm.DefaultServer.Client.GetInstallManualAsync(farm.ProjectId, farm.DefaultServer.ServerId);

        var actualAppSettings = JsonSerializer.Deserialize<ServerInstallAppSettings>(install.AppSettingsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.AreEqual(Convert.ToBase64String(install.AppSettings.Secret), Convert.ToBase64String(actualAppSettings.Secret));
        Assert.AreEqual(install.AppSettings.HttpAccessServer.BaseUrl, actualAppSettings.HttpAccessServer.BaseUrl);
        Assert.IsTrue(install.LinuxCommand.Contains("x64.sh"));
        Assert.IsTrue(install.WindowsCommand.Contains("x64.ps1"));
    }

    [TestMethod]
    public async Task ServerInstallByUserName()
    {
        var farm = await ServerFarmDom.Create();
        try
        {
            await farm.DefaultServer.Client.InstallBySshUserPasswordAsync(farm.ProjectId, farm.DefaultServer.ServerId,
                new ServerInstallBySshUserPasswordParams { HostName = "127.0.0.1", UserName = "user", Password = "pass" });
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);

        }

        try
        {
            await farm.DefaultServer.Client.InstallBySshUserKeyAsync(farm.ProjectId, farm.DefaultServer.ServerId,
                new ServerInstallBySshUserKeyParams { HostName = "127.0.0.1", UserName = "user", UserKey = TestResource.test_ssh_key });
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_should_not_create_for_another_project_farm()
    {
        var p1Farm = await ServerFarmDom.Create();
        var p2Farm = await ServerFarmDom.Create();

        try
        {
            await p1Farm.TestInit.ServersClient.CreateAsync(p1Farm.ProjectId,
                new ServerCreateParams
                {
                    ServerName = $"{Guid.NewGuid()}",
                    ServerFarmId = p2Farm.ServerFarmId
                });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_should_not_update_to_another_project_farm()
    {
        var p1Farm = await ServerFarmDom.Create();
        var p2Farm = await ServerFarmDom.Create();

        try
        {
            await p1Farm.DefaultServer.Update(new ServerUpdateParams
            {
                ServerFarmId = new PatchOfNullableGuid { Value = p2Farm.ServerFarmId }
            });

            Assert.Fail($"{nameof(NotExistsException)} was expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task GetStatusSummary()
    {
        var sampler = await ServerFarmDom.Create(serverCount: 0);
        sampler.TestInit.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;
        sampler.TestInit.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(2) / 3;

        // lost
        var sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 10 });
        await Task.Delay(2000);

        // active 2
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

        // notInstalled 4
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);
        await sampler.AddNewServer(false);

        // idle1
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0 });

        // idle2
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0 });

        // idle3
        sampleServer = await sampler.AddNewServer();
        await sampleServer.UpdateStatus(new ServerStatus { SessionCount = 0 });

        var liveUsageSummary = await sampler.TestInit.ServersClient.GetStatusSummaryAsync(sampler.TestInit.ProjectId);
        Assert.AreEqual(10, liveUsageSummary.TotalServerCount);
        Assert.AreEqual(2, liveUsageSummary.ActiveServerCount);
        Assert.AreEqual(4, liveUsageSummary.NotInstalledServerCount);
        Assert.AreEqual(1, liveUsageSummary.LostServerCount);
        Assert.AreEqual(3, liveUsageSummary.IdleServerCount);
        Assert.AreEqual(250, liveUsageSummary.TunnelSendSpeed);
        Assert.AreEqual(400, liveUsageSummary.TunnelReceiveSpeed);
    }

    [TestMethod]
    public async Task GetStatusHistory()
    {
        var farm = await ServerFarmDom.Create();
        var res = await farm.TestInit.ServersClient.GetStatusHistoryAsync(farm.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }

    [TestMethod]
    public async Task Crud_AccessPoints()
    {
        var testInit = await TestInit.Create();
        var farm = await ServerFarmDom.Create(testInit, serverCount: 0);

        var accessPoint1 = await testInit.NewAccessPoint();
        var accessPoint2 = await testInit.NewAccessPoint();

        // create server
        var serverDom = await farm.AddNewServer(new ServerCreateParams
        {
            AccessPoints = new[] { accessPoint1, accessPoint2 }
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
        var oldConfig = (await serverDom.UpdateStatus(serverDom.ServerInfo.Status)).ConfigCode;
        var accessPoint3 = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint3 } }
        });
        var newConfig = (await serverDom.UpdateStatus(serverDom.ServerInfo.Status)).ConfigCode;
        Assert.AreNotEqual(oldConfig, newConfig);

        await serverDom.Reload();
        Assert.AreEqual(1, serverDom.Server.AccessPoints.ToArray().Length);
        var accessPoint3B = serverDom.Server.AccessPoints.ToArray()[0];
        Assert.AreEqual(accessPoint3.IpAddress, accessPoint3B.IpAddress);
        Assert.AreEqual(accessPoint3.TcpPort, accessPoint3B.TcpPort);
        Assert.AreEqual(accessPoint3.UdpPort, accessPoint3B.UdpPort);
        Assert.AreEqual(accessPoint3.AccessPointMode, accessPoint3B.AccessPointMode); // first group must be default
        Assert.AreEqual(accessPoint3.IsListen, accessPoint3B.IsListen); // first group must be default
    }
}