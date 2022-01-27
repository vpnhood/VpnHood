using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;

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
        //-----------
        // check: Create
        //-----------
        var serverController = TestInit1.CreateServerController();
        var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var server1A = await serverController.Create(TestInit1.ProjectId, server1ACreateParam);
        var install1A = await serverController.InstallByManual(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(0, server1A.Secret.Length);

        //-----------
        // check: Get
        //-----------
        var serverData1 = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(server1ACreateParam.ServerName, serverData1.Server.ServerName);
        Assert.AreEqual(0, serverData1.Server.Secret.Length);
        Assert.AreEqual(ServerState.NotInstalled, serverData1.State);

        // ServerState.Configuring
        var agentController = TestInit1.CreateAgentController(server1A.ServerId);
        var serverInfo = await TestInit.NewServerInfo();
        serverInfo.Status.SessionCount = 0;
        await agentController.ConfigureServer(serverInfo);
        serverData1 = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        // ServerState.Idle
        await agentController.UpdateServerStatus(serverInfo.Status);
        serverData1 = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Idle, serverData1.State);

        // ServerState.Active
        await agentController.UpdateServerStatus(TestInit.NewServerStatus());
        serverData1 = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Active, serverData1.State);

        // ServerState.ConfigPending
        await serverController.Reconfigure(TestInit1.ProjectId, server1A.ServerId);
        serverData1 = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        //-----------
        // check: Update (Don't change Secret)
        //-----------
        var server1CUpdateParam = new ServerUpdateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId2, GenerateNewSecret = false };
        await serverController.Update(TestInit1.ProjectId, server1A.ServerId, server1CUpdateParam);
        var server1C = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        var install1C = await serverController.InstallByManual(TestInit1.ProjectId, server1A.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);
        Assert.AreEqual(server1CUpdateParam.ServerName, server1C.Server.ServerName);
        Assert.AreEqual(server1CUpdateParam.AccessPointGroupId, server1C.Server.AccessPointGroupId);
        Assert.IsTrue(server1C.AccessPoints?.All(x => x.AccessPointGroupId == TestInit1.AccessPointGroupId2));

        //-----------
        // check: Update (change Secret)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { GenerateNewSecret = true };
        await serverController.Update(TestInit1.ProjectId, server1A.ServerId, server1CUpdateParam);
        install1C = await serverController.InstallByManual(TestInit1.ProjectId, server1A.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);

        //-----------
        // check: Update (null serverFarmId)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { AccessPointGroupId = new Patch<Guid?>(null) };
        await serverController.Update(TestInit1.ProjectId, server1A.ServerId, server1CUpdateParam);
        server1C = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
        Assert.IsNull(server1C.Server.AccessPointGroupId);

        //-----------
        // check: List
        //-----------
        var servers = await serverController.List(TestInit1.ProjectId);
        Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1C.Server.ServerName && x.Server.ServerId == server1A.ServerId));
        Assert.IsTrue(servers.All(x => x.Server.Secret.Length == 0));
    }

    [TestMethod]
    public async Task Quota()
    {
        //-----------
        // check: Create
        //-----------
        var serverController = TestInit2.CreateServerController();
        await serverController.Create(TestInit2.ProjectId, new ServerCreateParams { ServerName = $"Guid.NewGuid()" });
        var servers = await serverController.List(TestInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.ServerCount = servers.Length;
        try
        {
            await serverController.Create(TestInit2.ProjectId, new ServerCreateParams
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
        var serverController = TestInit1.CreateServerController();
        var serverInstall = await serverController.InstallByManual(TestInit1.ProjectId, TestInit1.ServerId1);
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
            var serverController = TestInit1.CreateServerController();
            await serverController.Create(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        try
        {
            var serverController = TestInit1.CreateServerController();
            var server = await serverController.Create(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            await serverController.Update(TestInit1.ProjectId, server.ServerId,
                new ServerUpdateParams() { AccessPointGroupId = TestInit2.AccessPointGroupId1 });

            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }
}