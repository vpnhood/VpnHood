using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerClientTest : ClientTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        var serverClient = new ServerClient(TestInit1.Http);
        var serverData = await serverClient.GetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        var oldConfigCode = serverData.Server.ConfigCode;
        await serverClient.ReconfigureAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        serverData = await serverClient.GetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.AreNotEqual(oldConfigCode, serverData.Server.ConfigCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverClient = new ServerClient(testInit.Http);
        var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var server1A = await serverClient.CreateAsync(testInit.ProjectId, server1ACreateParam);

        var install1A = await serverClient.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);

        //-----------
        // check: Get
        //-----------
        var serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(server1ACreateParam.ServerName, serverData1.Server.ServerName);
        Assert.AreEqual(ServerState.NotInstalled, serverData1.State);

        // ServerState.Configuring
        var agentClient = testInit.CreateAgentClient(server1A.ServerId);
        var serverInfo = await testInit.NewServerInfo();
        serverInfo.Status.SessionCount = 0;
        var serverConfig = await agentClient.Server_Configure(serverInfo);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        // ServerState.Idle
        serverInfo.Status.ConfigCode = serverConfig.ConfigCode;
        await agentClient.Server_UpdateStatus(serverInfo.Status);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Idle, serverData1.State);

        // ServerState.Active
        await agentClient.Server_UpdateStatus(TestInit.NewServerStatus(serverConfig.ConfigCode));
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Active, serverData1.State);

        // ServerState.Configuring
        await serverClient.ReconfigureAsync(testInit.ProjectId, server1A.ServerId);
        serverData1 = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

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
        var install1C = await serverClient.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);
        Assert.AreEqual(server1CUpdateParam.ServerName.Value, server1C.Server.ServerName);
        Assert.AreEqual(server1CUpdateParam.AccessPointGroupId.Value, server1C.Server.AccessPointGroupId);
        Assert.IsTrue(server1C.AccessPoints?.All(x => x.AccessPointGroupId == testInit.AccessPointGroupId2));

        //-----------
        // check: Update (change Secret)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { GenerateNewSecret = new PatchOfBoolean { Value = true } };
        await serverClient.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        install1C = await serverClient.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);

        //-----------
        // check: Update (null serverFarmId)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = null } };
        await serverClient.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        server1C = await serverClient.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.IsNull(server1C.Server.AccessPointGroupId);

        //-----------
        // check: List
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
        var serverClient = new ServerClient(testInit2.Http);
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
        catch (ApiException ex) when (ex.ExceptionType?.Contains("QuotaException")==true)
        {
            // Ignore
        }
    }

    [TestMethod]
    public async Task ServerInstallManual()
    {
        var serverClient = new ServerClient(TestInit1.Http);
        var serverInstall = await serverClient.InstallByManualAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        Assert.IsFalse(Util.IsNullOrEmpty(serverInstall.AppSettings.Secret));
        Assert.IsFalse(string.IsNullOrEmpty(serverInstall.AppSettings.RestAccessServer.Authorization));
        Assert.IsNotNull(serverInstall.AppSettings.RestAccessServer.BaseUrl);
        Assert.IsNotNull(serverInstall.LinuxCommand);
    }

    [TestMethod]
    public async Task ServerInstallByUserName()
    {
        var serverClient = new ServerClient(TestInit1.Http);
        try
        {
            await serverClient.InstallBySshUserPasswordAsync(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserPasswordParams{HostName = "127.0.0.1", UserName = "user", Password = "pass"});
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("SocketException") ==true)
        {
            // ignore
        }

        try
        {
            await serverClient.InstallBySshUserKeyAsync(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserKeyParams{HostName = "127.0.0.1", UserName = "user", UserKey = TestResource.test_ssh_key});
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("SocketException") == true)
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
            var serverClient = new ServerClient(TestInit1.Http);
            await serverClient.CreateAsync(TestInit1.ProjectId,
                new ServerCreateParams
                    {ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = testInit2.AccessPointGroupId1});
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("NotExistsException") == true)
        {
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var testInit2 = await TestInit.Create();

        try
        {
            var serverClient = new ServerClient(TestInit1.Http);
            var server = await serverClient.CreateAsync(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            await serverClient.UpdateAsync(TestInit1.ProjectId, server.ServerId,
                new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid{Value = testInit2.AccessPointGroupId1 }});

            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("NotExistsException") == true)
        {
        }
    }
}