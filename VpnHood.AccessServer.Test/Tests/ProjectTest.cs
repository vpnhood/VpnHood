using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ProjectTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var projectsClient = testInit.ProjectsClient;
        var projectId = Guid.NewGuid();
        var project1A = await projectsClient.CreateAsync(projectId);
        Assert.AreEqual(projectId, project1A.ProjectId);

        //-----------
        // Check: Project is created
        //-----------
        var project1B = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(projectId, project1B.ProjectId);

        //-----------
        // Check: Update
        //-----------
        var updateParams = new ProjectUpdateParams
        {
            GoogleAnalyticsTrackId = new PatchOfString { Value = Guid.NewGuid().ToString() },
            ProjectName = new PatchOfString { Value = Guid.NewGuid().ToString() },
            TrackClientIp = new PatchOfBoolean { Value = false },
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.Nothing },
        };
        await projectsClient.UpdateAsync(projectId, updateParams);
        var project1C = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(projectId, project1C.ProjectId);
        Assert.AreEqual(project1C.GaTrackId, updateParams.GoogleAnalyticsTrackId.Value);
        Assert.AreEqual(project1C.ProjectName, updateParams.ProjectName.Value);
        Assert.AreEqual(project1C.TrackClientIp, updateParams.TrackClientIp.Value);
        Assert.AreEqual(project1C.TrackClientRequest, updateParams.TrackClientRequest.Value);

        //-----------
        // Check: Update
        //-----------
        updateParams = new ProjectUpdateParams
        {
            TrackClientIp = new PatchOfBoolean { Value = true },
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.LocalPort },
        };
        await projectsClient.UpdateAsync(projectId, updateParams);
        project1C = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(project1C.TrackClientIp, updateParams.TrackClientIp.Value);
        Assert.AreEqual(project1C.TrackClientRequest, updateParams.TrackClientRequest.Value);


        //-----------
        // Check: default group is created
        //-----------
        var serverFarms = await testInit.ServerFarmsClient.ListAsync(projectId);
        Assert.IsTrue(serverFarms.Count > 0);

        //-----------
        // Check: a public and private token is created
        //-----------
        var accessTokens = await testInit.AccessTokensClient.ListAsync(projectId);
        Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
        Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));

        //-----------
        // Check: Admin, Guest permission groups
        //-----------
        var authRepo = testInit.Scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();
        var rolePermissions = await authRepo.SecureObject_GetRolePermissionGroups(project1A.ProjectId);
        var adminRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
        var guestRole = rolePermissions.Single(x => x.PermissionGroupId == PermissionGroups.ProjectViewer.PermissionGroupId);

        Assert.AreEqual(Resource.ProjectOwners, adminRole.Role?.RoleName);
        Assert.AreEqual(Resource.ProjectViewers, guestRole.Role?.RoleName);

        //-----------
        // Check: All project
        //-----------
        var userProjects = await projectsClient.ListAsync();
        Assert.IsTrue(userProjects.Any(x => x.ProjectId == projectId));
    }

    [TestMethod]
    public async Task Invalidate_agent_cache_after_update()
    {
        var sampler = await ServerFarmDom.Create();
        var sampleAccessToken = await sampler.CreateAccessToken();
        await sampleAccessToken.CreateSession();

        var newProjectName = Guid.NewGuid().ToString();
        await sampler.TestInit.ProjectsClient.UpdateAsync(sampler.ProjectId, new ProjectUpdateParams
        {
            ProjectName = new PatchOfString { Value = newProjectName }
        });

        var server = await sampler.TestInit.CacheService.GetServer(sampler.Servers[0].ServerId);
        Assert.AreEqual(newProjectName, server.Project?.ProjectName);
    }

    [TestMethod]
    public async Task MaxUserProjects()
    {
        var testInit = await TestInit.Create();
        await testInit.SetHttpUser(testInit.UserSystemAdmin1.Email!);
        var userClient = new UserClient(testInit.Http);
        var user1 = await userClient.GetAsync(testInit.User1.UserId);
        await userClient.UpdateAsync(user1.UserId, new UserUpdateParams { MaxProjects = new PatchOfInteger { Value = 2 } });

        await testInit.SetHttpUser(testInit.User1.Email!);
        await testInit.ProjectsClient.CreateAsync();
        await testInit.ProjectsClient.CreateAsync();
        try
        {
            await testInit.ProjectsClient.CreateAsync();
            Assert.Fail($"{nameof(QuotaException)} is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task GetUsage()
    {
        var farm = await ServerFarmDom.Create();
        var accessTokenDom1 = await farm.CreateAccessToken();
        var accessTokenDom2 = await farm.CreateAccessToken();

        var sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage();
       
        sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage();

        sessionDom = await accessTokenDom2.CreateSession();
        await sessionDom.AddUsage();

        sessionDom = await accessTokenDom2.CreateSession();
        await sessionDom.AddUsage();

        await farm.TestInit.Sync();

        var res = await farm.TestInit.ProjectsClient.GetUsageAsync(farm.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(4, res.DeviceCount);
    }
}