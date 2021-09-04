using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ServerEndPointControllerTest : ControllerTest
    {

        [TestMethod]
        public async Task Crud()
        {
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPoint1 = TestInit1.HostEndPointNew1.ToString();
            var serverEndPoint1 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                new ServerEndPointCreateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, MakeDefault = true });

            //-----------
            // check: accessTokenGroupId is created
            //-----------
            var serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.AreEqual(serverEndPoint1B.PublicEndPoint, serverEndPoint1.PublicEndPoint);
            Assert.IsTrue(serverEndPoint1B.IsDefault); // first group must be default

            //-----------
            // check: error for delete default serverEndPoint
            //-----------
            try
            {
                await serverEndPointController.Delete(TestInit1.ProjectId, publicEndPoint1);
                Assert.Fail("Should not be able to delete default endPoint!");
            }
            catch (InvalidOperationException) { }

            //-----------
            // check: delete no default endPoint
            //-----------

            // change default
            var publicEndPoint2 = TestInit1.HostEndPointNew2;
            var serverEndPoint2 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint2.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, MakeDefault = true });
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);

            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");

            // delete
            await serverEndPointController.Delete(TestInit1.ProjectId, publicEndPoint1);
            try
            {
                await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
                Assert.Fail("EndPoint should not exist!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }

            //-----------
            // check: create no default endPoint
            //-----------
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                new ServerEndPointCreateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, MakeDefault = false });
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");
            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");

            //-----------
            // check: first created endPoint in group must be default even if isDefault is false
            //-----------
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var accessTokenGroup = await accessTokenGroupController.Create(TestInit1.ProjectId, null);
            var publicEndPoint3 = TestInit1.HostEndPointNew3;
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint3.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = accessTokenGroup.AccessTokenGroupId, MakeDefault = false });
            var serverEndPoint3 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint3.ToString());
            Assert.IsTrue(serverEndPoint3.IsDefault);

            //-----------
            // check: make default
            //-----------
            await serverEndPointController.Update(
                TestInit1.ProjectId,
                publicEndPoint1,
                new ServerEndPointUpdateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, MakeDefault = true });
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.IsTrue(serverEndPoint1B.IsDefault);

            //-----------
            // check: update without new certificate
            //-----------
            var privateEndPoint = await TestInit.NewEndPoint();
            await serverEndPointController.Update(TestInit1.ProjectId, publicEndPoint1, new ServerEndPointUpdateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, PrivateEndPoint = privateEndPoint.ToString(),  MakeDefault = true });
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.AreEqual(privateEndPoint.ToString(), serverEndPoint1B.PrivateEndPoint);
            Assert.IsTrue(serverEndPoint1B.IsDefault);
        }
    }
}
