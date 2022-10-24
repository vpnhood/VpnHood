using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ProjectClientTest : ClientTest
{
    [TestMethod]
    public async Task Crud()
    {
        var projectClient = new ProjectClient(TestInit1.Http);
        var projectId = Guid.NewGuid();
        var project1A = await projectClient.CreateAsync(projectId);
        Assert.AreEqual(projectId, project1A.ProjectId);

        //-----------
        // Check: Project is created
        //-----------
        var project1B = await projectClient.GetAsync(projectId);
        Assert.AreEqual(projectId, project1B.ProjectId);

        //-----------
        // Check: default group is created
        //-----------
        var accessPointGroupClient = new AccessPointGroupClient(TestInit1.Http);
        var accessPointGroups = await accessPointGroupClient.ListAsync(projectId);
        Assert.IsTrue(accessPointGroups.Count > 0);

        //-----------
        // Check: a public and private token is created
        //-----------
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
        var accessTokens = await accessTokenClient.ListAsync(projectId);
        Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
        Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));

        //-----------
        // Check: Admin, Guest permission groups
        //-----------
        var authRepo = TestInit1.Scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();
        var rolePermissions = await authRepo.SecureObject_GetRolePermissionGroups(project1A.ProjectId);
        var adminRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
        var guestRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectViewer.PermissionGroupId);

        Assert.AreEqual(Resource.ProjectOwners, adminRole.Role?.RoleName);
        Assert.AreEqual(Resource.ProjectViewers, guestRole.Role?.RoleName);

        //-----------
        // Check: All project
        //-----------
        var userProjects = await projectClient.ListAsync();
        Assert.IsTrue(userProjects.Any(x => x.ProjectId == projectId));
    }

    [TestMethod]
    public async Task MaxUserProjects()
    {
        await TestInit1.SetHttpUser(TestInit1.UserSystemAdmin1.Email!);
        var userClient = new UserClient(TestInit1.Http);
        var user1 = await userClient.GetAsync(TestInit1.User1.UserId);
        await userClient.UpdateAsync(user1.UserId, new UserUpdateParams { MaxProjects = new PatchOfInteger { Value = 2 } });

        await TestInit1.SetHttpUser(TestInit1.User1.Email!);
        var projectClient = new ProjectClient(TestInit1.Http);
        await projectClient.CreateAsync();
        await projectClient.CreateAsync();
        try
        {
            await projectClient.CreateAsync();
            Assert.Fail($"{nameof(QuotaException)} is expected!");
        }
        catch (ApiException ex) 
        {
            Assert.AreEqual(typeof(QuotaException).FullName, ex.ExceptionType);
        }
    }

}