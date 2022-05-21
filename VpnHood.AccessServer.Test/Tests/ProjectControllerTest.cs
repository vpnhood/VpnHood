using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ProjectControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        var projectController = new ProjectController(TestInit1.Http);
        var projectId = Guid.NewGuid();
        var project1A = await projectController.CreateAsync(projectId);
        Assert.AreEqual(projectId, project1A.ProjectId);

        //-----------
        // Check: Project is created
        //-----------
        var project1B = await projectController.GetAsync(projectId);
        Assert.AreEqual(projectId, project1B.ProjectId);

        //-----------
        // Check: default group is created
        //-----------
        var accessPointGroupController = new AccessPointGroupController(TestInit1.Http);
        var accessPointGroups = await accessPointGroupController.ListAsync(projectId);
        Assert.IsTrue(accessPointGroups.Count > 0);

        //-----------
        // Check: a public and private token is created
        //-----------
        var accessTokenController = new AccessTokenController(TestInit1.Http);
        var accessTokens = await accessTokenController.ListAsync(projectId);
        Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
        Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));

        //-----------
        // Check: Admin, Guest permission groups
        //-----------
        await using var vhContext = TestInit1.Scope.ServiceProvider.GetRequiredService<VhContext>();
        var rolePermissions = await vhContext.AuthManager.SecureObject_GetRolePermissionGroups(project1A.ProjectId);
        var adminRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
        var guestRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectViewer.PermissionGroupId);

        Assert.AreEqual(Resource.ProjectOwners, adminRole.Role?.RoleName);
        Assert.AreEqual(Resource.ProjectViewers, guestRole.Role?.RoleName);

        //-----------
        // Check: All project
        //-----------
        var userProjects = await projectController.ListAsync();
        Assert.IsTrue(userProjects.Any(x => x.ProjectId == projectId));
    }

    [TestMethod]
    public async Task MaxUserProjects()
    {
        TestInit1.SetHttpUser(TestInit1.UserSystemAdmin1.Email!);
        var userController = new UserController(TestInit1.Http);
        var user1 = await userController.GetAsync(TestInit1.User1.UserId);
        await userController.UpdateAsync(user1.UserId, new UserUpdateParams { MaxProjects = new PatchOfInteger { Value = 2 } });

        TestInit1.SetHttpUser(TestInit1.User1.Email!);
        var projectController = new ProjectController(TestInit1.Http);
        await projectController.CreateAsync();
        await projectController.CreateAsync();
        try
        {
            await projectController.CreateAsync();
            Assert.Fail($"{nameof(QuotaException)} is expected!");
        }
        catch (ApiException ex) when(ex.IsQuotaException)
        {
            // Ignore
        }
    }

}