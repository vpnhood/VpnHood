using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessPointGroupControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task CRUD_public()
        {
            AccessPointGroupController accessPointGroupController = TestInit1.CreateAccessPointGroupController();

            //-----------
            // check: create non default
            //-----------
            var accessPointGroup1Z = new AccessPointGroup { AccessPointGroupName = $"group 1 {Guid.NewGuid()}" };
            var accessPointGroup1A = await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams { AccessPointGroupName = accessPointGroup1Z.AccessPointGroupName });
            var accessPointGroup1B = await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
            Assert.AreEqual(accessPointGroup1Z.AccessPointGroupName, accessPointGroup1A.AccessPointGroupName);
            Assert.AreEqual(accessPointGroup1Z.AccessPointGroupName,
                accessPointGroup1B.AccessPointGroup.AccessPointGroupName);
            Assert.IsFalse(accessPointGroup1A.IsDefault);
            Assert.AreNotEqual(Guid.Empty, accessPointGroup1B.AccessPointGroup.CertificateId);

            //-----------
            // check: create default
            //-----------
            var accessPointGroup2Z = new AccessPointGroup { AccessPointGroupName = $"group 2 {Guid.NewGuid()}" };
            var accessPointGroup2A = await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams { AccessPointGroupName = accessPointGroup2Z.AccessPointGroupName, MakeDefault = true });
            Assert.AreEqual(accessPointGroup2A.AccessPointGroupName, accessPointGroup2Z.AccessPointGroupName);
            Assert.IsTrue(accessPointGroup2A.IsDefault);

            //-----------
            // check: update without changing default
            //-----------
            accessPointGroup1Z.AccessPointGroupName = $"group1_new_name_{Guid.NewGuid()}";
            await accessPointGroupController.Update(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId, new AccessPointGroupUpdateParams { AccessPointGroupName = accessPointGroup1Z.AccessPointGroupName });
            accessPointGroup1A = (await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId)).AccessPointGroup;
            Assert.AreEqual(accessPointGroup1Z.AccessPointGroupName, accessPointGroup1A.AccessPointGroupName);
            Assert.IsFalse(accessPointGroup1A.IsDefault);

            //-----------
            // check: update and just make default
            //-----------
            await accessPointGroupController.Update(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId, new AccessPointGroupUpdateParams{MakeDefault = true});
            accessPointGroup1A =
                (await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId))
                .AccessPointGroup;
            Assert.AreEqual(accessPointGroup1Z.AccessPointGroupName, accessPointGroup1A.AccessPointGroupName);
            Assert.IsTrue(accessPointGroup1A.IsDefault);

            //-----------
            // check: AlreadyExists exception
            //-----------
            try
            {
                await accessPointGroupController.Update(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId,
                    new AccessPointGroupUpdateParams{AccessPointGroupName = accessPointGroup2A.AccessPointGroupName});
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            {
            }

            //-----------
            // check: Error for deleting a default group
            //-----------
            try
            {
                await accessPointGroupController.Delete(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
                Assert.Fail("Exception Expected!");
            }
            catch (InvalidOperationException)
            {
            }

            //-----------
            // check: deleting a  default group
            //-----------
            await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams{MakeDefault = true});
            await accessPointGroupController.Delete(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
            try
            {
                await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (ex is not AssertFailedException)
            {
            }
        }
    }
}