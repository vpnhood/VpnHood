using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointControllerTest : ControllerTest
{

    [TestMethod]
    public async Task Crud()
    {
        var accessPointController = new AccessPointController(TestInit1.Http);
        var publicEndPoint1 = await TestInit1.NewEndPoint();
        var createParam1 = new AccessPointCreateParams
        {
            ServerId = TestInit1.ServerId1,
            IpAddress = publicEndPoint1.Address.ToString(),
            AccessPointGroupId = TestInit1.AccessPointGroupId2,
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };
        var accessPoint1 = await accessPointController.CreateAsync(TestInit1.ProjectId, createParam1);

        //-----------
        // check: accessPointGroupId is created
        //-----------
        var accessPoint1B = await accessPointController.GetAsync(TestInit1.ProjectId, accessPoint1.AccessPointId);
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
            IpAddress = new PatchOfString { Value = await TestInit1.NewIpV4String() },
            AccessPointGroupId = new PatchOfGuid { Value = TestInit1.AccessPointGroupId2 },
            AccessPointMode = new PatchOfAccessPointMode { Value = AccessPointMode.Private },
            TcpPort = new PatchOfInteger { Value = accessPoint1B.TcpPort + 1 },
            UdpPort = new PatchOfInteger { Value = accessPoint1B.TcpPort + 1 },
            IsListen = new PatchOfBoolean { Value = false }
        };
        await accessPointController.UpdateAsync(TestInit1.ProjectId, accessPoint1B.AccessPointId, updateParams);
        accessPoint1B = await accessPointController.GetAsync(TestInit1.ProjectId, accessPoint1B.AccessPointId);
        Assert.AreEqual(updateParams.IpAddress.Value, accessPoint1B.IpAddress);
        Assert.AreEqual(updateParams.TcpPort.Value, accessPoint1B.TcpPort);
        Assert.AreEqual(updateParams.UdpPort.Value, accessPoint1B.UdpPort);
        Assert.AreEqual(updateParams.AccessPointMode.Value, accessPoint1B.AccessPointMode);
        Assert.AreEqual(updateParams.IsListen.Value, accessPoint1B.IsListen);

        //-----------
        // check: delete 
        //-----------
        await accessPointController.DeleteAsync(TestInit1.ProjectId, accessPoint1.AccessPointId);
        try
        {
            await accessPointController.GetAsync(TestInit1.ProjectId, accessPoint1.AccessPointId);
            Assert.Fail("AccessPoint should not exist!");
        }
        catch (ApiException ex) when (ex.IsNotExistsException) { }
    }

    [TestMethod]
    public async Task Error_server_farm_must_set_to_manual()
    {
        //Create a server
        var serverController = new ServerController(TestInit1.Http);
        var server = await serverController.CreateAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = null });
        var serverConfigCode = server.ConfigCode;

        var accessPointController = new AccessPointController(TestInit1.Http);
        var publicEndPoint1 = await TestInit1.NewEndPoint();
        var createParam1 = new AccessPointCreateParams
        {
            ServerId = server.ServerId,
            IpAddress = publicEndPoint1.Address.ToString(),
            AccessPointGroupId = TestInit1.AccessPointGroupId2,
            TcpPort = publicEndPoint1.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        };

        //-----------
        // check: Schedule config after creating new access point and 
        //-----------
        var accessPoint = await accessPointController.CreateAsync(TestInit1.ProjectId, createParam1);
        server = (await serverController.GetAsync(TestInit1.ProjectId, server.ServerId)).Server;
        Assert.AreNotEqual(serverConfigCode, server.ConfigCode);
        serverConfigCode = server.ConfigCode;


        //-----------
        // check: InvalidOperationException when server is not manual
        //-----------
        await serverController.UpdateAsync(TestInit1.ProjectId, server.ServerId, new ServerUpdateParams
        {
            AccessPointGroupId = new PatchOfNullableGuid { Value = TestInit1.AccessPointGroupId2 }
        });
        server = (await serverController.GetAsync(TestInit1.ProjectId, server.ServerId)).Server;
        Assert.AreNotEqual(serverConfigCode, server.ConfigCode);
        serverConfigCode = server.ConfigCode;

        try
        {
            createParam1.IpAddress = (await TestInit1.NewIpV4()).ToString();
            await accessPointController.CreateAsync(TestInit1.ProjectId, createParam1);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType!.Contains("InvalidOperationException")) { }
        Assert.AreEqual(serverConfigCode, server.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for update
        //-----------
        try
        {
            await accessPointController.UpdateAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
                new AccessPointUpdateParams { TcpPort = new PatchOfInteger { Value = accessPoint.TcpPort + 1 } });
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType!.Contains("InvalidOperationException")) { }
        Assert.AreEqual(serverConfigCode, server.ConfigCode);

        //-----------
        // check: InvalidOperationException when server is not manual for remove
        //-----------
        try
        {
            await accessPointController.DeleteAsync(TestInit1.ProjectId, accessPoint.AccessPointId);
            Assert.Fail("InvalidOperationException was expected!");
        }
        catch (ApiException ex) when (ex.ExceptionType!.Contains("InvalidOperationException")) { }
        Assert.AreEqual(serverConfigCode, server.ConfigCode);
    }
}