using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;
using ServerCreateParams = VpnHood.AccessServer.DTOs.ServerCreateParams;
using ServerInstallBySshUserKeyParams = VpnHood.AccessServer.DTOs.ServerInstallBySshUserKeyParams;
using ServerInstallBySshUserPasswordParams = VpnHood.AccessServer.DTOs.ServerInstallBySshUserPasswordParams;
using ServerUpdateParams = VpnHood.AccessServer.DTOs.ServerUpdateParams;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        var serverController = TestInit1.CreateServerController();
        var serverData = await serverController.Get(TestInit1.ProjectId, TestInit1.ServerId1);
        var oldConfigCode = serverData.Server.ConfigCode;
        await serverController.Reconfigure(TestInit1.ProjectId, TestInit1.ServerId1);

        serverData = await serverController.Get(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.AreNotEqual(oldConfigCode, serverData.Server.ConfigCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverController = new ServerController(testInit.Http);
        var server1ACreateParam = new Api.ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var server1A = await serverController.ServersPostAsync(testInit.ProjectId, server1ACreateParam);

        var install1A = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);

        //-----------
        // check: Get
        //-----------
        var serverData1 = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(server1ACreateParam.ServerName, serverData1.Server.ServerName);
        Assert.AreEqual(ServerState.NotInstalled, serverData1.State);

        // ServerState.Configuring
        var agentController = testInit.CreateAgentController2(server1A.ServerId);
        var serverInfo = await testInit.NewServerInfo2();
        serverInfo.Status.SessionCount = 0;
        await agentController.ConfigureAsync(serverInfo);
        serverData1 = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        // ServerState.Idle
        await agentController.StatusAsync(serverInfo.Status);
        serverData1 = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Idle, serverData1.State);

        // ServerState.Active
        await agentController.StatusAsync(TestInit.NewServerStatus2());
        serverData1 = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Active, serverData1.State);

        // ServerState.ConfigPending
        await serverController.ReconfigureAsync(testInit.ProjectId, server1A.ServerId);
        serverData1 = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        //-----------
        // check: Update (Don't change Secret)
        //-----------
        var server1CUpdateParam = new Api.ServerUpdateParams
        {
            ServerName = new StringPatch { Value = $"{Guid.NewGuid()}" },
            AccessPointGroupId = new GuidNullablePatch { Value = testInit.AccessPointGroupId2 },
            GenerateNewSecret = new BooleanPatch { Value = false }
        };
        await serverController.ServersPatchAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        var server1C = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        var install1C = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);
        Assert.AreEqual(server1CUpdateParam.ServerName.Value, server1C.Server.ServerName);
        Assert.AreEqual(server1CUpdateParam.AccessPointGroupId.Value, server1C.Server.AccessPointGroupId);
        Assert.IsTrue(server1C.AccessPoints?.All(x => x.AccessPointGroupId == testInit.AccessPointGroupId2));

        //-----------
        // check: Update (change Secret)
        //-----------
        server1CUpdateParam = new Api.ServerUpdateParams { GenerateNewSecret = new BooleanPatch { Value = true } };
        await serverController.ServersPatchAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        install1C = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);

        //-----------
        // check: Update (null serverFarmId)
        //-----------
        server1CUpdateParam = new Api.ServerUpdateParams { AccessPointGroupId = new GuidNullablePatch { Value = null } };
        await serverController.ServersPatchAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        server1C = await serverController.ServersGetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.IsNull(server1C.Server.AccessPointGroupId);

        //-----------
        // check: List
        //-----------
        var servers = await serverController.ServersGetAsync(testInit.ProjectId);
        Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1C.Server.ServerName && x.Server.ServerId == server1A.ServerId));
    }

    [TestMethod]
    public async Task Quota()
    {
        var testInit2 = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverController = testInit2.CreateServerController();
        await serverController.Create(testInit2.ProjectId, new ServerCreateParams { ServerName = "Guid.NewGuid()" });
        var servers = await serverController.List(testInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.ServerCount = servers.Length;
        try
        {
            await serverController.Create(testInit2.ProjectId, new ServerCreateParams
            {
                ServerName = $"{Guid.NewGuid()}"
            });
            Assert.Fail($"{nameof(QuotaException)} is expected");
        }
        catch (QuotaException)
        {
            // Ignore
        }
    }

    [TestMethod]
    public async Task ServerInstallManual()
    {
        var serverController = new ServerController(TestInit1.Http);
        var serverInstall = await serverController.InstallByManualAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.IsFalse(Util.IsNullOrEmpty(serverInstall.AppSettings.Secret));
        Assert.IsFalse(string.IsNullOrEmpty(serverInstall.AppSettings.RestAccessServer.Authorization));
        Assert.IsNotNull(serverInstall.AppSettings.RestAccessServer.BaseUrl);
        Assert.IsNotNull(serverInstall.LinuxCommand);
    }

    [TestMethod]
    public async Task ServerInstallByUserName()
    {
        var serverController = TestInit1.CreateServerController();
        try
        {
            await serverController.InstallBySshUserPassword(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserPasswordParams("127.0.0.1", "user", "pass"));
        }
        catch (SocketException)
        {
            // ignore
        }

        try
        {
            await serverController.InstallBySshUserKey(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserKeyParams("127.0.0.1", "user", TestResource.test_ssh_key));
        }
        catch (SocketException)
        {
            // ignore
        }
    }

    [TestMethod]
    public async Task Validate_create()
    {
        try
        {
            var testInit2 = await TestInit.Create();
            var serverController = TestInit1.CreateServerController();
            await serverController.Create(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var testInit2 = await TestInit.Create();

        try
        {
            var serverController = TestInit1.CreateServerController();
            var server = await serverController.Create(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            await serverController.Update(TestInit1.ProjectId, server.ServerId,
                new ServerUpdateParams() { AccessPointGroupId = testInit2.AccessPointGroupId1 });

            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }
}