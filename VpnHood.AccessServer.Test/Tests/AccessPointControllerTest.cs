﻿using System;
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
            var publicEndPoint1 = TestInit1.HostEndPointNew1;
            var accessPoint1 = await accessPointController.Create(TestInit1.ProjectId, 
                new AccessPointCreateParams { PublicEndPoint = publicEndPoint1, AccessPointGroupId = TestInit1.AccessPointGroupId2, MakeDefault = true });

            //-----------
            // check: accessPointGroupId is created
            //-----------
            var accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());
            Assert.AreEqual(accessPoint1B.PublicEndPoint, accessPoint1.PublicEndPoint);
            Assert.IsTrue(accessPoint1B.IsDefault); // first group must be default

            //-----------
            // check: error for delete default accessPoint
            //-----------
            try
            {
                await accessPointController.Delete(TestInit1.ProjectId, publicEndPoint1.ToString());
                Assert.Fail("Should not be able to delete default accessPoint!");
            }
            catch (InvalidOperationException) { }

            //-----------
            // check: delete no default accessPoint
            //-----------

            // change default
            var publicEndPoint2 = TestInit1.HostEndPointNew2;
            var accessPoint2 = await accessPointController.Create(TestInit1.ProjectId,
                new AccessPointCreateParams { PublicEndPoint = publicEndPoint2, AccessPointGroupId = TestInit1.AccessPointGroupId2, MakeDefault = true });
            accessPoint1 = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());

            Assert.IsTrue(accessPoint2.IsDefault, "AccessPoint2 must be default");
            Assert.IsFalse(accessPoint1.IsDefault, "AccessPoint1 should not be default");

            // delete
            await accessPointController.Delete(TestInit1.ProjectId, publicEndPoint1.ToString());
            try
            {
                await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());
                Assert.Fail("AccessPoint should not exist!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }

            //-----------
            // check: create no default accessPoint
            //-----------
            await accessPointController.Create(TestInit1.ProjectId,
                new AccessPointCreateParams { PublicEndPoint = publicEndPoint1, AccessPointGroupId = TestInit1.AccessPointGroupId2, MakeDefault = false });
            accessPoint1 = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());
            Assert.IsFalse(accessPoint1.IsDefault, "AccessPoint1 should not be default");
            Assert.IsTrue(accessPoint2.IsDefault, "AccessPoint2 must be default");

            //-----------
            // check: first created accessPoint in group must be default even if isDefault is false
            //-----------
            var accessPointGroupController = TestInit1.CreateAccessPointGroupController();
            var accessPointGroup = await accessPointGroupController.Create(TestInit1.ProjectId, null);
            var publicEndPoint3 = TestInit1.HostEndPointNew3;
            await accessPointController.Create(TestInit1.ProjectId, 
                new AccessPointCreateParams { PublicEndPoint = publicEndPoint3, AccessPointGroupId = accessPointGroup.AccessPointGroupId, MakeDefault = false });
            var accessPoint3 = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint3.ToString());
            Assert.IsTrue(accessPoint3.IsDefault);

            //-----------
            // check: make default
            //-----------
            await accessPointController.Update(
                TestInit1.ProjectId,
                publicEndPoint1.ToString(),
                new AccessPointUpdateParams { AccessPointGroupId = TestInit1.AccessPointGroupId2, MakeDefault = true });
            accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());
            Assert.IsTrue(accessPoint1B.IsDefault);

            //-----------
            // check: update without new certificate
            //-----------
            var privateEndPoint = await TestInit.NewEndPoint();
            await accessPointController.Update(TestInit1.ProjectId, publicEndPoint1.ToString(), new AccessPointUpdateParams { AccessPointGroupId = TestInit1.AccessPointGroupId2, PrivateEndPoint = privateEndPoint.ToString(),  MakeDefault = true });
            accessPoint1B = await accessPointController.Get(TestInit1.ProjectId, publicEndPoint1.ToString());
            Assert.AreEqual(privateEndPoint.ToString(), accessPoint1B.PrivateEndPoint);
            Assert.IsTrue(accessPoint1B.IsDefault);
        }
    }
}
