using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;

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
            var createParam1 = new AccessPointCreateParams
            {
                PublicIpAddress = publicEndPoint1.Address, 
                PrivateIpAddress = await TestInit.NewIp(),
                TcpPort = publicEndPoint1.Port,
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                IncludeInAccessToken = true
            };
            var accessPoint1 = await accessPointController.Create(TestInit1.ProjectId, TestInit1.ServerId1, createParam1);

            //-----------
            // check: accessPointGroupId is created
            //-----------
            var accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, accessPoint1.AccessPointId);
            Assert.AreEqual(createParam1.PublicIpAddress.ToString(), accessPoint1B.PublicIpAddress);
            Assert.AreEqual(createParam1.PrivateIpAddress.ToString(), accessPoint1B.PrivateIpAddress);
            Assert.AreEqual(createParam1.TcpPort, accessPoint1B.TcpPort);
            Assert.AreEqual(createParam1.UdpPort, accessPoint1B.UdpPort);
            Assert.AreEqual(createParam1.IncludeInAccessToken, accessPoint1B.IncludeInAccessToken); // first group must be default

            //-----------
            // check: update 
            //-----------
            var updateParams = new AccessPointUpdateParams
            {
                PublicIpAddress = (await TestInit.NewIp()).ToString(),
                PrivateIpAddress = (await TestInit.NewIp()).ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                IncludeInAccessToken = false,
                TcpPort = accessPoint1B.TcpPort + 1,
                UdpPort = accessPoint1B.TcpPort + 1
            };
            await accessPointController.Update(TestInit1.ProjectId, accessPoint1B.AccessPointId, updateParams);
            accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, accessPoint1B.AccessPointId);
            Assert.AreEqual(updateParams.PublicIpAddress.Value, accessPoint1B.PublicIpAddress);
            Assert.AreEqual(updateParams.PrivateIpAddress.Value, accessPoint1B.PrivateIpAddress);
            Assert.AreEqual(updateParams.TcpPort.Value, accessPoint1B.TcpPort);
            Assert.AreEqual(updateParams.UdpPort.Value, accessPoint1B.UdpPort);
            Assert.AreEqual(updateParams.IncludeInAccessToken.Value, accessPoint1B.IncludeInAccessToken); // first group must be default

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
