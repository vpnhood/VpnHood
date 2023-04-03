using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Security;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class TeamUserTest
{
    [TestMethod]
    public async Task Crud()
    {
        using var testInit = await TestInit.Create();

        // create
        var addUserParam = new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectAdmin.RoleId
        };
        var userRole = await testInit.TeamClient.AddUserAsync(testInit.ProjectId, addUserParam);
        Assert.AreEqual(addUserParam.Email, userRole.User.Email);
        Assert.AreEqual(addUserParam.RoleId, userRole.Role.RoleId);

        // get
        userRole = await testInit.TeamClient.GetUserAsync(testInit.ProjectId, userRole.User.UserId);
        Assert.AreEqual(addUserParam.Email, userRole.User.Email);
        Assert.AreEqual(addUserParam.RoleId, userRole.Role.RoleId);

        // update 
        var teamUserUpdate = new TeamUpdateUserParam
        {
            RoleId = new PatchOfGuid { Value = Roles.ProjectReader.RoleId }
        };
        userRole = await testInit.TeamClient.UpdateUserAsync(testInit.ProjectId, userRole.User.UserId, teamUserUpdate);
        Assert.AreEqual(addUserParam.Email, userRole.User.Email);
        Assert.AreEqual(teamUserUpdate.RoleId.Value, userRole.Role.RoleId);

        userRole = await testInit.TeamClient.GetUserAsync(testInit.ProjectId, userRole.User.UserId);
        Assert.AreEqual(addUserParam.Email, userRole.User.Email);
        Assert.AreEqual(teamUserUpdate.RoleId.Value, userRole.Role.RoleId);

        // delete
        await testInit.TeamClient.RemoveUserAsync(testInit.ProjectId, userRole.User.UserId);
        try
        {
            await testInit.TeamClient.GetUserAsync(testInit.ProjectId, userRole.User.UserId);
            Assert.Fail("NotExistsException ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }


    [TestMethod]
    public async Task User_already_exists()
    {
        using var testInit = await TestInit.Create();

        // create
        var addUserParam = new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectAdmin.RoleId
        };
        await testInit.TeamClient.AddUserAsync(testInit.ProjectId, addUserParam);

        try
        {
            await testInit.TeamClient.AddUserAsync(testInit.ProjectId, addUserParam);
            Assert.Fail("AlreadyExistsException ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(AlreadyExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task List()
    {
        using var testInit = await TestInit.Create();

        // create
        var userRole1 = await testInit.TeamClient.AddUserAsync(testInit.ProjectId, new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectAdmin.RoleId
        });

        var userRole2 = await testInit.TeamClient.AddUserAsync(testInit.ProjectId, new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectReader.RoleId
        });

        var userRoles = await testInit.TeamClient.ListUsersAsync(testInit.ProjectId);
        var userRole1B = userRoles.Single(x => x.User.UserId == userRole1.User.UserId);
        var userRole2B = userRoles.Single(x => x.User.UserId == userRole2.User.UserId);

        Assert.AreEqual(userRole1.User.Email, userRole1B.User.Email);
        Assert.AreEqual(userRole1.User.UserId, userRole1B.User.UserId);
        Assert.AreEqual(userRole1.Role.RoleId, userRole1B.Role.RoleId);

        Assert.AreEqual(userRole2.User.Email, userRole2B.User.Email);
        Assert.AreEqual(userRole2.User.UserId, userRole2B.User.UserId);
        Assert.AreEqual(userRole2.Role.RoleId, userRole2B.Role.RoleId);
    }

    [TestMethod]
    public async Task Owner_should_not_remove_update_himself()
    {
        using var testInit = await TestInit.Create();

        // ---------------
        // Check: update
        // ---------------
        try
        {
            await testInit.TeamClient.UpdateUserAsync(testInit.ProjectId, testInit.UserProjectOwner.UserId, new TeamUpdateUserParam
            {
                RoleId = new PatchOfGuid { Value = Roles.ProjectAdmin.RoleId }
            });
            Assert.Fail("InvalidOperationException ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }

        // ---------------
        // Check: remove
        // ---------------
        try
        {
            await testInit.TeamClient.RemoveUserAsync(testInit.ProjectId, testInit.UserProjectOwner.UserId);
            Assert.Fail("InvalidOperationException ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Admin_should_not_add_remove_update_owner()
    {
        using var testInit = await TestInit.Create();
        var userRole = await testInit.TeamClient.AddUserAsync(testInit.ProjectId, new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectAdmin.RoleId
        });
        await testInit.SetHttpUser(userRole.User);

        // ---------------
        // Check: add
        // ---------------
        try
        {
            await testInit.TeamClient.AddUserAsync(testInit.ProjectId, new TeamAddUserParam
            {
                Email = $"{Guid.NewGuid()}@gmail.com",
                RoleId = Roles.ProjectOwner.RoleId
            });
            Assert.Fail($"{nameof(UnauthorizedAccessException)} ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(ex.StatusCode, (int)HttpStatusCode.Forbidden);
        }

        // ---------------
        // Check: update
        // ---------------
        try
        {
            await testInit.TeamClient.UpdateUserAsync(testInit.ProjectId, userRole.User.UserId, new TeamUpdateUserParam
            {
                RoleId = new PatchOfGuid { Value = Roles.ProjectOwner.RoleId }
            });
            Assert.Fail($"{nameof(UnauthorizedAccessException)} ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(ex.StatusCode, (int)HttpStatusCode.Forbidden);
        }

        // ---------------
        // Check: remove
        // ---------------
        try
        {
            await testInit.TeamClient.RemoveUserAsync(testInit.ProjectId, testInit.UserProjectOwner.UserId);
            Assert.Fail($"{nameof(UnauthorizedAccessException)} ws expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(ex.StatusCode, (int)HttpStatusCode.Forbidden);
        }
    }

    [TestMethod]
    public async Task Owner_should_remove_other()
    {
        using var testInit = await TestInit.Create();

        // create
        var userRole = await testInit.TeamClient.AddUserAsync(testInit.ProjectId, new TeamAddUserParam
        {
            Email = $"{Guid.NewGuid()}@gmail.com",
            RoleId = Roles.ProjectOwner.RoleId
        });

        await testInit.TeamClient.RemoveUserAsync(testInit.ProjectId, userRole.User.UserId);
    }

    [TestMethod]
    public async Task AddApiKey()
    {

    }
}