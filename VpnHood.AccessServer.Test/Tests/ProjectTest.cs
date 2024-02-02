using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
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
        var project1A = await projectsClient.CreateAsync();
        var projectId = project1A.ProjectId;
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
            GaMeasurementId = new PatchOfString { Value = Guid.NewGuid().ToString() },
            GaApiSecret = new PatchOfString { Value = Guid.NewGuid().ToString() },
            ProjectName = new PatchOfString { Value = Guid.NewGuid().ToString() },
        };
        await projectsClient.UpdateAsync(projectId, updateParams);
        var project1C = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(projectId, project1C.ProjectId);
        Assert.AreEqual(project1C.GaMeasurementId, updateParams.GaMeasurementId.Value);
        Assert.AreEqual(project1C.GaApiSecret, updateParams.GaApiSecret.Value);
        Assert.AreEqual(project1C.ProjectName, updateParams.ProjectName.Value);

        //-----------
        // Check: Update
        //-----------
        updateParams = new ProjectUpdateParams
        {
            GaMeasurementId = new PatchOfString { Value = Guid.NewGuid().ToString() },
            GaApiSecret = new PatchOfString { Value = Guid.NewGuid().ToString() },
        };
        await projectsClient.UpdateAsync(projectId, updateParams);
        project1C = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(project1C.GaMeasurementId, updateParams.GaMeasurementId.Value);
        Assert.AreEqual(project1C.GaApiSecret, updateParams.GaApiSecret.Value);


        //-----------
        // Check: default group is created
        //-----------
        var serverFarms = await testInit.ServerFarmsClient.ListAsync(projectId);
        Assert.IsTrue(serverFarms.Count > 0);

        //-----------
        // Check: a public and private token is created
        //-----------
        var accessTokens = await testInit.AccessTokensClient.ListAsync(projectId);
        Assert.IsTrue(accessTokens.Items.Any(x => x.AccessToken.IsPublic));
        Assert.IsTrue(accessTokens.Items.Any(x => !x.AccessToken.IsPublic));

        //-----------
        // Check: All project
        //-----------
        var userProjects = await testInit.TeamClient.ListCurrentUserProjectsAsync();
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

        var server = await sampler.TestInit.AgentTestApp.CacheService.GetServer(sampler.Servers[0].ServerId);
        Assert.AreEqual(newProjectName, server.Project?.ProjectName);
    }

    [TestMethod]
    public async Task MaxUserProjects()
    {
        // create first project
        var testInit = await TestInit.Create();

        // create next project the using same user
        await testInit.ProjectsClient.CreateAsync();
        try
        {
            QuotaConstants.ProjectCount = 2;
            await testInit.ProjectsClient.CreateAsync();
            Assert.Fail($"{nameof(QuotaException)} is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Owner_is_created()
    {
        var testInit = await TestInit.Create();
        var userRoles = await testInit.TeamClient.ListUserRolesAsync(testInit.ProjectId.ToString());
        Assert.AreEqual(1, userRoles.TotalCount);

        var userRole = userRoles.Items.First();
        Assert.AreEqual(testInit.ProjectOwnerApiKey.UserId, userRole.User?.UserId);
        Assert.AreEqual(Roles.ProjectOwner.RoleId, userRole.Role.RoleId);
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