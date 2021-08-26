using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessTokenGroupController_Test : ControllerTest
    {
        [TestMethod]
        public async Task CRUD_public()
        {
            AccessTokenGroupController accessTokenGroupController = TestInit.CreateAccessTokenGroupController();

            //-----------
            // check: create non default
            //-----------
            var accessTokenGroup1Z = new AccessTokenGroup { AccessTokenGroupName = $"group 1 {Guid.NewGuid()}" };
            var accessTokenGroup1A = await accessTokenGroupController.Create(TestInit1.ProjectId, accessTokenGroup1Z.AccessTokenGroupName);
            var accessTokenGroup1B = await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId);
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1B.AccessTokenGroup.AccessTokenGroupName);
            Assert.IsFalse(accessTokenGroup1A.IsDefault);

            //-----------
            // check: create default
            //-----------
            var accessTokenGroup2Z = new AccessTokenGroup { AccessTokenGroupName = $"group 2 {Guid.NewGuid()}" };
            var accessTokenGroup2A = await accessTokenGroupController.Create(TestInit1.ProjectId, accessTokenGroup2Z.AccessTokenGroupName, true);
            Assert.AreEqual(accessTokenGroup2A.AccessTokenGroupName, accessTokenGroup2Z.AccessTokenGroupName);
            Assert.IsTrue(accessTokenGroup2A.IsDefault);

            //-----------
            // check: update without changing default
            //-----------
            accessTokenGroup1Z.AccessTokenGroupName = $"group1_new_name_{Guid.NewGuid()}";
            await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId, accessTokenGroup1Z.AccessTokenGroupName);
            accessTokenGroup1A = (await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId)).AccessTokenGroup;
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.IsFalse(accessTokenGroup1A.IsDefault);

            //-----------
            // check: update and just make default
            //-----------
            await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId, makeDefault: true);
            accessTokenGroup1A = (await accessTokenGroupController.Get(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId)).AccessTokenGroup;
            Assert.AreEqual(accessTokenGroup1Z.AccessTokenGroupName, accessTokenGroup1A.AccessTokenGroupName);
            Assert.IsTrue(accessTokenGroup1A.IsDefault);

            //-----------
            // check: AlreadyExists exception
            //-----------
            try
            {
                await accessTokenGroupController.Update(TestInit1.ProjectId, accessTokenGroup1A.AccessTokenGroupId, accessTokenGroup2A.AccessTokenGroupName);
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            { }
        }
    }
}
