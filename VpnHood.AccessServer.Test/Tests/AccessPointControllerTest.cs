using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessPointControllerTest : ControllerTest
    {

        [TestMethod]
        public async Task Crud()
        {
            var accessPointController = TestInit1.CreateAccessPointController();
            var publicEndPoint1 = await TestInit.NewEndPoint();
            var createParam1 = new AccessPointCreateParams(publicEndPoint1.Address)
            {
                TcpPort = publicEndPoint1.Port,
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = true
            };
            var accessPoint1 = await accessPointController.Create(TestInit1.ProjectId, TestInit1.ServerId1, createParam1);

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
    }
}
