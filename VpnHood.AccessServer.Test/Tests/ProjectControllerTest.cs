using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ProjectControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Crud()
        {
            var projectController = TestInit1.CreateProjectController();
            var projectId = Guid.NewGuid();
            var project1A = await projectController.Create(projectId);
            Assert.AreEqual(projectId, project1A.ProjectId);

            //-----------
            // Check: Project is created
            //-----------
            var project1B = await projectController.Get(projectId);
            Assert.AreEqual(projectId, project1B.ProjectId);

            //-----------
            // Check: default group is created
            //-----------
            var accessPointGroupController = TestInit1.CreateAccessPointGroupController();
            var accessPointGroups = await accessPointGroupController.List(projectId);
            Assert.IsTrue(accessPointGroups.Length > 0);

            //-----------
            // Check: a public and private token is created
            //-----------
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(projectId);
            Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
            Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));

            //-----------
            // Check: Admin, Guest permission groups
            //-----------
            await using var vhContext = new VhContext();
            var rolePermissions = await vhContext.AuthManager.SecureObject_GetRolePermissionGroups(project1A.ProjectId);
            var adminRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
            var guestRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectViewer.PermissionGroupId);

            Assert.AreEqual(Resource.ProjectOwners, adminRole.Role?.RoleName);
            Assert.AreEqual(Resource.ProjectViewers, guestRole.Role?.RoleName);

            //-----------
            // Check: All project
            //-----------
            var userProjects = await projectController.List();
            Assert.IsTrue(userProjects.Any(x=>x.ProjectId == projectId));
        }

        [TestMethod]
        public async Task MaxUserProjects()
        {
            var userController = TestInit1.CreateUserController(TestInit1.UserSystemAdmin1.Email);
            var user1 = await userController.Get(TestInit1.User1.UserId);
            await userController.Update(user1.UserId, new UserUpdateParams { MaxProjects = 2 });

            var projectController = TestInit1.CreateProjectController(TestInit1.User1.Email);
            await projectController.Create();
            await projectController.Create();
            try
            {
                await projectController.Create();
                Assert.Fail($"{nameof(QuotaException)} is expected!");
            }
            catch (QuotaException)
            {
                // Ignore
            }
        }

    }
}