using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
            Assert.IsTrue(accessPointGroups.Any(x => x.AccessPointGroup.IsDefault));

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
            await using VhContext vhContext = new();
            var rolePermissions = await vhContext.AuthManager.SecureObject_GetRolePermissionGroups(project1A.ProjectId);
            var adminRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.Admin.PermissionGroupId);
            var guestRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.Guest.PermissionGroupId);

            Assert.AreEqual(Resource.Administrators, adminRole.Role?.RoleName);
            Assert.AreEqual(Resource.Guests, guestRole.Role?.RoleName);

            //-----------
            // Check: All project
            //-----------
            var userProjects = await projectController.List();
            Assert.AreEqual(1, userProjects.Length);
            Assert.AreEqual(userProjects[0].ProjectId, projectId);
        }
    }
}