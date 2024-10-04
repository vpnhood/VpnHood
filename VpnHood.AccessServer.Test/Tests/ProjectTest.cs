using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.ApiClients;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ProjectTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testApp = await TestApp.Create();
        var projectsClient = testApp.ProjectsClient;
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
        var updateParams = new ProjectUpdateParams {
            GaMeasurementId = new PatchOfString { Value = Guid.NewGuid().ToString() },
            GaApiSecret = new PatchOfString { Value = Guid.NewGuid().ToString() },
            ProjectName = new PatchOfString { Value = Guid.NewGuid().ToString() }
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
        updateParams = new ProjectUpdateParams {
            GaMeasurementId = new PatchOfString { Value = Guid.NewGuid().ToString() },
            GaApiSecret = new PatchOfString { Value = Guid.NewGuid().ToString() }
        };
        await projectsClient.UpdateAsync(projectId, updateParams);
        project1C = await projectsClient.GetAsync(projectId);
        Assert.AreEqual(project1C.GaMeasurementId, updateParams.GaMeasurementId.Value);
        Assert.AreEqual(project1C.GaApiSecret, updateParams.GaApiSecret.Value);


        //-----------
        // Check: default group is created
        //-----------
        var serverFarms = await testApp.ServerFarmsClient.ListAsync(projectId);
        Assert.IsTrue(serverFarms.Count > 0);

        //-----------
        // Check: a public and private token is created
        //-----------
        var accessTokens = await testApp.AccessTokensClient.ListAsync(projectId);
        Assert.IsTrue(accessTokens.Items.Any(x => x.AccessToken.IsPublic));
        Assert.IsTrue(accessTokens.Items.Any(x => !x.AccessToken.IsPublic));

        //-----------
        // Check: All project
        //-----------
        var userProjects = await testApp.TeamClient.ListCurrentUserProjectsAsync();
        Assert.IsTrue(userProjects.Any(x => x.ProjectId == projectId));
    }

    [TestMethod]
    public async Task Invalidate_agent_cache_after_update()
    {
        var farmDom = await ServerFarmDom.Create();
        var accessTokenDom = await farmDom.CreateAccessToken();
        await accessTokenDom.CreateSession();

        var gaApiSecret = Guid.NewGuid().ToString();
        await farmDom.TestApp.ProjectsClient.UpdateAsync(farmDom.ProjectId, new ProjectUpdateParams {
            GaApiSecret = new PatchOfString { Value = gaApiSecret }
        });

        var project = await farmDom.TestApp.AgentTestApp.CacheService.GetProject(farmDom.ProjectId);
        Assert.AreEqual(gaApiSecret, project.GaApiSecret);
    }

    [TestMethod]
    public async Task MaxUserProjects()
    {
        // create first project
        var testApp = await TestApp.Create();

        // create next project the using same user
        await testApp.ProjectsClient.CreateAsync();
        try {
            QuotaConstants.ProjectCount = 2;
            await testApp.ProjectsClient.CreateAsync();
            Assert.Fail($"{nameof(QuotaException)} is expected!");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Owner_is_created()
    {
        var testApp = await TestApp.Create();
        var userRoles = await testApp.TeamClient.ListUserRolesAsync(testApp.ProjectId.ToString());
        Assert.AreEqual(1, userRoles.TotalCount);

        var userRole = userRoles.Items.First();
        Assert.AreEqual(testApp.ProjectOwnerApiKey.UserId, userRole.User?.UserId);
        Assert.AreEqual(Roles.ProjectOwner.RoleId, userRole.Role.RoleId);
    }
}