using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointControllerTest : ControllerTest
{

    [TestMethod]
    public async Task Crud()
    {
        var accessPointController = TestInit1.CreateAccessPointController();
        var publicEndPoint1 = await TestInit.NewEndPoint();
        var createParam1 = new AccessPointCreateParams(
            TestInit1.ServerId1,
            publicEndPoint1.Address,
            TestInit1.AccessPointGroupId2)
        {
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };
        var accessPoint1 = await accessPointController.Create(TestInit1.ProjectId, createParam1);

        //-----------
        // check: accessPointGroupId is created
        //-----------
        var accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, accessPoint1.AccessPointId);
        Assert.AreEqual(createParam1.IpAddress.ToString(), accessPoint1B.IpAddress);
        Assert.AreEqual(createParam1.TcpPort, accessPoint1B.TcpPort);
        Assert.AreEqual(createParam1.UdpPort, accessPoint1B.UdpPort);
        Assert.AreEqual(createParam1.AccessPointMode, accessPoint1B.AccessPointMode); // first group must be default
        Assert.AreEqual(createParam1.IsListen, accessPoint1B.IsListen); // first group must be default

        //-----------
        // check: update 
        //-----------
        var updateParams = new AccessPointUpdateParams
        {
            IpAddress = (await TestInit.NewIpV4()).ToString(),
            AccessPointGroupId = TestInit1.AccessPointGroupId2,
            AccessPointMode = AccessPointMode.Private,
            TcpPort = accessPoint1B.TcpPort + 1,
            UdpPort = accessPoint1B.TcpPort + 1,
            IsListen = false
        };
        await accessPointController.Update(TestInit1.ProjectId, accessPoint1B.AccessPointId, updateParams);
        accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, accessPoint1B.AccessPointId);
        Assert.AreEqual(updateParams.IpAddress.Value, accessPoint1B.IpAddress);
        Assert.AreEqual(updateParams.TcpPort.Value, accessPoint1B.TcpPort);
        Assert.AreEqual(updateParams.UdpPort.Value, accessPoint1B.UdpPort);
        Assert.AreEqual(updateParams.AccessPointMode.Value, accessPoint1B.AccessPointMode);
        Assert.AreEqual(updateParams.IsListen.Value, accessPoint1B.IsListen);

        //-----------
        // check: delete 
        //-----------
        await accessPointController.Delete(TestInit1.ProjectId, accessPoint1.AccessPointId);
        try
        {
            await accessPointController.Get(TestInit1.ProjectId, accessPoint1.AccessPointId);
            Assert.Fail("AccessPoint should not exist!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
    }

    [TestMethod]
    public async Task Error_server_farm_must_set_to_manual()
    {
        //Create a server
        var serverController = TestInit1.CreateServerController();
        var server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams {AccessPointGroupId = null});
        var serverConfigCode = server.ConfigCode;

        var accessPointController = TestInit1.CreateAccessPointController();
        var publicEndPoint1 = await TestInit.NewEndPoint();
        var createParam1 = new AccessPointCreateParams(
            server.ServerId,
            publicEndPoint1.Address,
            TestInit1.AccessPointGroupId2)
        {
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };

        //-----------
        // check: Schedule config after creating new access point and 
        //-----------
        var accessPoint = await accessPointController.Create(TestInit1.ProjectId, createParam1);
        server = (await serverController.Get(TestInit1.ProjectId, server.ServerId)).Server;
        Assert.AreNotEqual(serverConfigCode, server.ConfigCode);
        serverConfigCode = server.ConfigCode;


        //-----------
        // check: InvalidOperationException when server is not manual
        //-----------
        await serverController.Update(TestInit1.ProjectId, server.ServerId, new ServerUpdateParams{AccessPointGroupId = TestInit1.AccessPointGroupId2 });
        server = (await serverController.Get(TestInit1.ProjectId, server.ServerId)).Server;
        Assert.AreNotEqual(serverConfigCode, server.ConfigCode);
        serverConfigCode = server.ConfigCode;

        try
        {
            createParam1.IpAddress = await TestInit.NewIpV4();
            await accessPointController.Create(TestInit1.ProjectId, createParam1);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (InvalidOperationException) {}
        Assert.AreEqual(serverConfigCode, server.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for update
        //-----------
        try
        {
            await accessPointController.Update(TestInit1.ProjectId, accessPoint.AccessPointId, new AccessPointUpdateParams{TcpPort = accessPoint .TcpPort+1});
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (InvalidOperationException) { }
        Assert.AreEqual(serverConfigCode, server.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for remove
        //-----------
        try
        {
            await accessPointController.Delete(TestInit1.ProjectId, accessPoint.AccessPointId);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (InvalidOperationException) { }
        Assert.AreEqual(serverConfigCode, server.ConfigCode);
    }
}