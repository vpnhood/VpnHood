﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Reconfig()
    {
        var serverController = new ServerController(TestInit1.Http);
        var serverData = await serverController.GetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
        var oldConfigCode = serverData.Server.ConfigCode;
        await serverController.ReconfigureAsync(TestInit1.ProjectId, TestInit1.ServerId1);

        serverData = await serverController.GetAsync(TestInit1.ProjectId, TestInit1.ServerId1);
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
        var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
        var server1A = await serverController.CreateAsync(testInit.ProjectId, server1ACreateParam);

        var install1A = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);

        //-----------
        // check: Get
        //-----------
        var serverData1 = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(server1ACreateParam.ServerName, serverData1.Server.ServerName);
        Assert.AreEqual(ServerState.NotInstalled, serverData1.State);

        // ServerState.Configuring
        var agentController = testInit.CreateAgentController(server1A.ServerId);
        var serverInfo = await testInit.NewServerInfo();
        serverInfo.Status.SessionCount = 0;
        var serverConfig = await agentController.ConfigureServerAsync(serverInfo);
        serverData1 = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Configuring, serverData1.State);

        // ServerState.Idle
        serverInfo.Status.ConfigCode = serverConfig.ConfigCode;
        await agentController.UpdateServerStatusAsync(serverInfo.Status);
        serverData1 = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Idle, serverData1.State);

        // ServerState.Active
        await agentController.UpdateServerStatusAsync(TestInit.NewServerStatus(serverConfig.ConfigCode));
        serverData1 = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.AreEqual(ServerState.Active, serverData1.State);

        // ServerState.Configuring
        await serverController.ReconfigureAsync(testInit.ProjectId, server1A.ServerId);
        serverData1 = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
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
        await serverController.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        var server1C = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        var install1C = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);
        Assert.AreEqual(server1CUpdateParam.ServerName.Value, server1C.Server.ServerName);
        Assert.AreEqual(server1CUpdateParam.AccessPointGroupId.Value, server1C.Server.AccessPointGroupId);
        Assert.IsTrue(server1C.AccessPoints?.All(x => x.AccessPointGroupId == testInit.AccessPointGroupId2));

        //-----------
        // check: Update (change Secret)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { GenerateNewSecret = new PatchOfBoolean { Value = true } };
        await serverController.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        install1C = await serverController.InstallByManualAsync(testInit.ProjectId, server1A.ServerId);
        CollectionAssert.AreNotEqual(install1A.AppSettings.Secret, install1C.AppSettings.Secret);

        //-----------
        // check: Update (null serverFarmId)
        //-----------
        server1CUpdateParam = new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = null } };
        await serverController.UpdateAsync(testInit.ProjectId, server1A.ServerId, server1CUpdateParam);
        server1C = await serverController.GetAsync(testInit.ProjectId, server1A.ServerId);
        Assert.IsNull(server1C.Server.AccessPointGroupId);

        //-----------
        // check: List
        //-----------
        var servers = await serverController.ListAsync(testInit.ProjectId);
        Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1C.Server.ServerName && x.Server.ServerId == server1A.ServerId));
    }

    [TestMethod]
    public async Task Quota()
    {
        var testInit2 = await TestInit.Create();

        //-----------
        // check: Create
        //-----------
        var serverController = new ServerController(testInit2.Http);
        await serverController.CreateAsync(testInit2.ProjectId, new ServerCreateParams { ServerName = "Guid.NewGuid()" });
        var servers = await serverController.ListAsync(testInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.ServerCount = servers.Count;
        try
        {
            await serverController.CreateAsync(testInit2.ProjectId, new ServerCreateParams
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
        var serverController = new ServerController(TestInit1.Http);
        try
        {
            await serverController.InstallBySshUserPasswordAsync(TestInit1.ProjectId, TestInit1.ServerId1,
                new ServerInstallBySshUserPasswordParams{HostName = "127.0.0.1", UserName = "user", Password = "pass"});
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("SocketException") ==true)
        {
            // ignore
        }

        try
        {
            await serverController.InstallBySshUserKeyAsync(TestInit1.ProjectId, TestInit1.ServerId1,
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
            var serverController = new ServerController(TestInit1.Http);
            await serverController.CreateAsync(TestInit1.ProjectId,
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
            var serverController = new ServerController(TestInit1.Http);
            var server = await serverController.CreateAsync(TestInit1.ProjectId,
                new ServerCreateParams { ServerName = $"{Guid.NewGuid()}", AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            await serverController.UpdateAsync(TestInit1.ProjectId, server.ServerId,
                new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid{Value = testInit2.AccessPointGroupId1 }});

            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType?.Contains("NotExistsException") == true)
        {
        }
    }
}