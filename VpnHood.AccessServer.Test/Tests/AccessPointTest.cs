using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointTest : BaseTest
{

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var farm = await AccessPointGroupDom.Create(testInit);

        var server = await testInit.ServersClient.CreateAsync(testInit.ProjectId, new ServerCreateParams
        {
            AccessPointGroupId = null
        });

        var accessPointClient = testInit.AccessPointsClient;
        var publicEndPoint1 = await testInit.NewEndPoint();
        var createParam1 = new AccessPointCreateParams
        {
            ServerId = server.ServerId,
            IpAddress = publicEndPoint1.Address.ToString(),
            AccessPointGroupId = farm.AccessPointGroupId,
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };
        var accessPoint1 = await accessPointClient.CreateAsync(testInit.ProjectId, createParam1);

        //-----------
        // check: accessPointGroupId is created
        //-----------
        var accessPoint1B = await accessPointClient.GetAsync(testInit.ProjectId, accessPoint1.AccessPointId);
        Assert.AreEqual(createParam1.IpAddress, accessPoint1B.IpAddress);
        Assert.AreEqual(createParam1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(createParam1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(createParam1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(createParam1.IsListen, accessPoint1B.IsListen); // first group must be default

        //-----------
        // check: update 
        //-----------
        var updateParams = new AccessPointUpdateParams
        {
            IpAddress = new PatchOfString { Value = await testInit.NewIpV4String() },
            AccessPointGroupId = new PatchOfGuid { Value = farm.AccessPointGroupId },
            AccessPointMode = new PatchOfAccessPointMode { Value = AccessPointMode.Private },
            TcpPort = new PatchOfInteger { Value = accessPoint1B.TcpPort + 1 },
            UdpPort = new PatchOfInteger { Value = accessPoint1B.TcpPort + 1 },
            IsListen = new PatchOfBoolean { Value = false }
        };
        await accessPointClient.UpdateAsync(testInit.ProjectId, accessPoint1B.AccessPointId, updateParams);
        accessPoint1B = await accessPointClient.GetAsync(testInit.ProjectId, accessPoint1B.AccessPointId);
        Assert.AreEqual(updateParams.IpAddress.Value, accessPoint1B.IpAddress);
        Assert.AreEqual(updateParams.TcpPort.Value, accessPoint1B.TcpPort);
        Assert.AreEqual(updateParams.UdpPort.Value, accessPoint1B.UdpPort);
        Assert.AreEqual(updateParams.AccessPointMode.Value, accessPoint1B.AccessPointMode);
        Assert.AreEqual(updateParams.IsListen.Value, accessPoint1B.IsListen);

        //-----------
        // check: delete 
        //-----------
        await accessPointClient.DeleteAsync(testInit.ProjectId, accessPoint1.AccessPointId);
        try
        {
            await accessPointClient.GetAsync(testInit.ProjectId, accessPoint1.AccessPointId);
            Assert.Fail("AccessPoint should not exist!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Error_server_farm_must_set_to_manual()
    {
        var farm = await AccessPointGroupDom.Create();
        var testInit = farm.TestInit;

        //Create a server
        var serverClient = testInit.ServersClient;
        var server = await serverClient.CreateAsync(testInit.ProjectId, new ServerCreateParams { AccessPointGroupId = null });
        var serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == server.ServerId);

        var serverConfigCode = serverModel.ConfigCode;

        var accessPointClient = testInit.AccessPointsClient;
        var publicEndPoint1 = await testInit.NewEndPoint();
        var createParam1 = new AccessPointCreateParams
        {
            ServerId = server.ServerId,
            IpAddress = publicEndPoint1.Address.ToString(),
            AccessPointGroupId = farm.AccessPointGroupId,
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };

        //-----------
        // check: Schedule config after creating new access point and 
        //-----------
        var accessPoint = await accessPointClient.CreateAsync(testInit.ProjectId, createParam1);
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == server.ServerId);
        Assert.AreNotEqual(serverConfigCode, serverModel.ConfigCode);
        serverConfigCode = serverModel.ConfigCode;


        //-----------
        // check: InvalidOperationException when server is not manual
        //-----------
        await serverClient.UpdateAsync(testInit.ProjectId, server.ServerId, new ServerUpdateParams
        {
            AccessPointGroupId = new PatchOfNullableGuid { Value = farm.AccessPointGroupId }
        });
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == server.ServerId);
        Assert.AreNotEqual(serverConfigCode, serverModel.ConfigCode);
        serverConfigCode = serverModel.ConfigCode;

        try
        {
            createParam1.IpAddress = (await testInit.NewIpV4()).ToString();
            await accessPointClient.CreateAsync(testInit.ProjectId, createParam1);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == server.ServerId);
        Assert.AreEqual(serverConfigCode, serverModel.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for update
        //-----------
        try
        {
            await accessPointClient.UpdateAsync(testInit.ProjectId, accessPoint.AccessPointId,
                new AccessPointUpdateParams { TcpPort = new PatchOfInteger { Value = accessPoint.TcpPort + 1 } });
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == server.ServerId);
        Assert.AreEqual(serverConfigCode, serverModel.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for remove
        //-----------
        try
        {
            await accessPointClient.DeleteAsync(testInit.ProjectId, accessPoint.AccessPointId);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }
        serverModel = await testInit.VhContext.Servers.SingleAsync(x => x.ServerId == server.ServerId);
        Assert.AreEqual(serverConfigCode, serverModel.ConfigCode);
    }
}