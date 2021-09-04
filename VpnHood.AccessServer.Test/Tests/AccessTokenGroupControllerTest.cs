using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessTokenGroupControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task CRUD_public()
        {
            AccessTokenGroupController accessTokenGroupController = TestInit.CreateAccessTokenGroupController();

            //-----------
            // check: create non default
            //-----------
            var accessTokenGroup1Z = new AccessTokenGroup { AccessTokenGroupName = $"group 1 {Guid.NewGuid()}" };
            var accessTokenGroup1A = await accessTokenGroupController.Create(TestInit1.ProjectId, new EndPointGroupCreateParams { AccessTokenGroupName = accessTokenGroup1Z.AccessTokenGroupName });
            var accessTokenGroup1B = await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId);
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName,
                accessTokenGroup1B.AccessTokenGroup.AccessTokenGroupName);
            Assert.IsFalse(accessTokenGroup1A.IsDefault);
            Assert.AreNotEqual(Guid.Empty, accessTokenGroup1B.AccessTokenGroup.CertificateId);

            //-----------
            // check: create default
            //-----------
            var accessTokenGroup2Z = new AccessTokenGroup { AccessTokenGroupName = $"group 2 {Guid.NewGuid()}" };
            var accessTokenGroup2A = await accessTokenGroupController.Create(TestInit1.ProjectId, new EndPointGroupCreateParams { AccessTokenGroupName = accessTokenGroup2Z.AccessTokenGroupName, MakeDefault = true });
            Assert.AreEqual(accessTokenGroup2A.AccessTokenGroupName, accessTokenGroup2Z.AccessTokenGroupName);
            Assert.IsTrue(accessTokenGroup2A.IsDefault);

            //-----------
            // check: update without changing default
            //-----------
            accessTokenGroup1Z.AccessTokenGroupName = $"group1_new_name_{Guid.NewGuid()}";
            await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId, new EndPointGroupUpdateParams { AccessTokenGroupName = accessTokenGroup1Z.AccessTokenGroupName });
            accessTokenGroup1A = (await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId)).AccessTokenGroup;
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.IsFalse(accessTokenGroup1A.IsDefault);

            //-----------
            // check: update and just make default
            //-----------
            await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId, new EndPointGroupUpdateParams{MakeDefault = true});
            accessTokenGroup1A =
                (await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId))
                .AccessTokenGroup;
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.IsTrue(accessTokenGroup1A.IsDefault);

            //-----------
            // check: AlreadyExists exception
            //-----------
            try
            {
                await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId,
                    new EndPointGroupUpdateParams{AccessTokenGroupName = accessTokenGroup2A.AccessTokenGroupName});
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
                await accessTokenGroupController.Delete(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId);
                Assert.Fail("Exception Expected!");
            }
            catch (InvalidOperationException)
            {
            }

            //-----------
            // check: deleting a  default group
            //-----------
            await accessTokenGroupController.Create(TestInit1.ProjectId, new EndPointGroupCreateParams{MakeDefault = true});
            await accessTokenGroupController.Delete(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId);
            try
            {
                await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId);
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (ex is not AssertFailedException)
            {
            }
        }
    }
}