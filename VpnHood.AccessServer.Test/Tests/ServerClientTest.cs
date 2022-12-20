using System;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerClientTest : ClientTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        var serverClient = new ServersClient(TestInit1.Http);
        var serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == TestInit1.ServerId1);
        var oldConfigCode = serverModel.ConfigCode;
        await serverClient.ReconfigureAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        //TestInit1.VhContext.ChangeTracker.Clear();
        serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == TestInit1.ServerId1);
        Assert.AreNotEqual(oldConfigCode, serverModel.ConfigCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverClient = testInit.ServersClient;
        var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var server1A = await serverClient.CreateAsync(testInit.ProjectId, server1ACreateParam);
        var install1A = await serverClient.GetInstallAppSettingsAsync(testInit.ProjectId, server1A.ServerId);

        //-----------
        // check: Get
        //-----------
        var serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(server1ACreateParam.ServerName, serverData1.Server.ServerName);
        Assert.AreEqual(ServerState.NotInstalled, serverData1.Server.ServerState);

        // ServerState.Configuring
        var agentClient = testInit.CreateAgentClient(server1A.ServerId);
        var serverInfo = await testInit.NewServerInfo();
        serverInfo.Status.SessionCount = 0;
        var serverConfig = await agentClient.Server_Configure(serverInfo);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.Server.ServerState);

        // ServerState.Idle
        serverInfo.Status.ConfigCode = serverConfig.ConfigCode;
        await agentClient.Server_UpdateStatus(serverInfo.Status);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Idle, serverData1.Server.ServerState);

        // ServerState.Active
        await agentClient.Server_UpdateStatus(TestInit.NewServerStatus(serverConfig.ConfigCode));
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Active, serverData1.Server.ServerState);

        // ServerState.Configuring
        await serverClient.ReconfigureAsync(testInit.ProjectId, server1A.ServerId);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.Server.ServerState);

        //-----------
        // check: Update (Don't change Secret)
        //-----------
        var server1CUpdateParam = new ServerUpdateParams
        {
            ServerName = new PatchOfString { Value = $"{Guid.NewGuid()}" },
            AccessPointGroupId = new PatchOfNullableGuid { Value = testInit.AccessPointGroupId2 },
            GenerateNewSecret = new PatchOfBoolean { Value = false }
        };
        await serverClient.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        var server1C = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        var install1C = await serverClient.GetInstallAppSettingsAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreEqual(install1A.Secret, install1C.Secret);
        Assert.AreEqual(server1CUpdateParam.ServerName.Value, server1C.Server.ServerName);
        Assert.AreEqual(server1CUpdateParam.AccessPointGroupId.Value, server1C.Server.AccessPointGroupId);
        Assert.IsTrue(server1C.AccessPoints.All(x => x.AccessPointGroupId == testInit.AccessPointGroupId2));

        //-----------
        // check: Update (change Secret)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { GenerateNewSecret = new PatchOfBoolean { Value = true } };
        await serverClient.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        install1C = await serverClient.GetInstallAppSettingsAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreNotEqual(install1A.Secret, install1C.Secret);

        //-----------
        // check: Update (null accessPointGroupId)
        //-----------
        server1CUpdateParam = new ServerUpdateParams
        {
            AccessPointGroupId = new PatchOfNullableGuid { Value = null }
        };
        await serverClient.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        server1C = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.IsNull(server1C.Server.AccessPointGroupId);

        //-----------
        // check: Get
        //-----------
        var servers = await serverClient.ListAsync(testInit.ProjectId);
        Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1C.Server.ServerName && x.Server.ServerId == server1A.ServerId));
    }

    [TestMethod]
    public async Task Quota()
    {
        var testInit2 = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverClient = testInit2.ServersClient;
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams { ServerName = "Guid.NewGuid()" });
        await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams { ServerName = "Guid.NewGuid()" });
        
        var servers = await serverClient.ListAsync(testInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.ServerCount = servers.Count;
        try
        {
            await serverClient.CreateAsync(testInit2.ProjectId, new ServerCreateParams
            {
                ServerName = $"{Guid.NewGuid()}"
            });
            Assert.Fail($"{nameof(QuotaException)} is expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Install_GetAppSettings()
    {
        var serverClient = TestInit1.ServersClient;
        var appSettings = await serverClient.GetInstallAppSettingsAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.IsFalse(Util.IsNullOrEmpty(appSettings.Secret));
        Assert.IsFalse(string.IsNullOrEmpty(appSettings.HttpAccessServer.Authorization));
        Assert.IsNotNull(appSettings.HttpAccessServer.BaseUrl);
    }

    [TestMethod]
    public async Task Install_GetAppSettingsJson()
    {
        var appSettings = await TestInit1.ServersClient
            .GetInstallAppSettingsAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        var appSettingsJson = await TestInit1.ServersClient
            .GetInstallAppSettingsJsonAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        var actualAppSettings = JsonSerializer.Deserialize<ServerInstallAppSettings>(appSettingsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.AreEqual(Convert.ToBase64String(appSettings.Secret), Convert.ToBase64String(actualAppSettings.Secret));
        Assert.AreEqual(appSettings.HttpAccessServer.BaseUrl, actualAppSettings.HttpAccessServer.BaseUrl);
    }

    [TestMethod]
    public async Task Install_GetInstallScriptForWindows()
    {
        var script = await TestInit1.ServersClient
            .GetInstallScriptForWindowsAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        Assert.IsTrue(script.Contains("x64.ps1"));
    }

    [TestMethod]
    public async Task Install_GetInstallScriptForLinux()
    {
        var script = await TestInit1.ServersClient
            .GetInstallScriptForLinuxAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        Assert.IsTrue(script.Contains("x64.sh"));
    }


    [TestMethod]
    public async Task ServerInstallByUserName()
    {
        var serverClient = TestInit1.ServersClient;
        try
        {
            await serverClient.InstallBySshUserPasswordAsync(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserPasswordParams { HostName = "127.0.0.1", UserName = "user", Password = "pass" });
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);

        }

        try
        {
            await serverClient.InstallBySshUserKeyAsync(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserKeyParams { HostName = "127.0.0.1", UserName = "user", UserKey = TestResource.test_ssh_key });
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(SocketException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_create()
    {
        try
        {
            var testInit2 = await TestInit.Create();
            var serverClient = TestInit1.ServersClient;
            await serverClient.CreateAsync(TestInit1.ProjectId,
                new ServerCreateParams
                { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var testInit2 = await TestInit.Create();

        try
        {
            var serverClient = TestInit1.ServersClient;
            var server = await serverClient.CreateAsync(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            await serverClient.UpdateAsync(TestInit1.ProjectId, server.ServerId,
                new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = testInit2.AccessPointGroupId1 } });

            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);

        }
    }

    [TestMethod]
    public async Task GetStatusSummary()
    {
        var sampler = await AccessPointGroupDom.Create(serverCount: 0);
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
        var res = await TestInit1.ServersClient.GetStatusHistoryAsync(TestInit1.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }
}