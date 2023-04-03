using System.Threading.Tasks;
using System;
using System.Linq;
using VpnHood.AccessServer.Security;
using GrayMint.Common.Exceptions;
using VpnHood.AccessServer.Dtos;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using VpnHood.AccessServer.DtoConverters;
using Role = VpnHood.AccessServer.Dtos.UserDtos.Role;
using UserRole = VpnHood.AccessServer.Dtos.UserDtos.UserRole;

namespace VpnHood.AccessServer.Services;

public class TeamService
{
    private readonly SimpleRoleProvider _simpleRoleProvider;
    private readonly SimpleUserProvider _simpleUserProvider;
    private readonly SubscriptionService _subscriptionService;

    public TeamService(
        SimpleRoleProvider simpleRoleProvider,
        SimpleUserProvider simpleUserProvider,
        SubscriptionService subscriptionService)
    {
        _simpleRoleProvider = simpleRoleProvider;
        _simpleUserProvider = simpleUserProvider;
        _subscriptionService = subscriptionService;
    }

    public async Task<UserRole> AddUser(Guid projectId, TeamAddUserParam addParam)
    {
        await _subscriptionService.VerifyAddUser(projectId);

        // create user if not found
        var user = await _simpleUserProvider.FindByEmail<UserExData>(addParam.Email);
        user ??= await _simpleUserProvider.Create(new UserCreateRequest<UserExData> { Email = addParam.Email });

        var userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(appId: projectId.ToString(), userId: user.UserId);
        if (userRoles.Any())
            throw new AlreadyExistsException("User already exists in your team");

        await _simpleRoleProvider.AddUser(roleId: addParam.RoleId, userId: user.UserId, appId: projectId.ToString());
        userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(userId: user.UserId, appId: projectId.ToString());
        return userRoles.Single(x => x.User.UserId == user.UserId).ToDto();
    }

    public async Task<UserRole> GetUser(Guid projectId, Guid userId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(userId: userId, appId: projectId.ToString());
        return userRoles.Single(x => x.User.UserId == userId).ToDto();
    }

    public async Task<UserRole> UpdateUser(Guid projectId, Guid userId, TeamUpdateUserParam updateParam)
    {
        var userRole = await GetUser(projectId, userId);

        if (updateParam.RoleId != null)
        {
            //remove the old roles
            await _simpleRoleProvider.RemoveUser(userRole.Role.RoleId, userId, projectId.ToString());

            // add to new role
            await _simpleRoleProvider.AddUser<UserExData>(updateParam.RoleId, userId, projectId.ToString());
        }

        userRole = await GetUser(projectId, userId);
        return userRole;

    }

    public async Task RemoveUser(Guid projectId, Guid userId)
    {
        var userRole = await GetUser(projectId, userId);

        // remove the user
        await _simpleRoleProvider.RemoveUser(userRole.Role.RoleId, userId, projectId.ToString());

        // remove user from repo if he is not in any role
        var allUserRoles = await _simpleRoleProvider.GetUserRoles(userId);
        if (!allUserRoles.Any())
            await _simpleUserProvider.Remove(userId);
    }

    public async Task<UserRole[]> ListUsers(Guid projectId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(appId: projectId.ToString());
        return userRoles.Select(x => x.ToDto()).ToArray();
    }

    public Task<Role[]> ListRoles(Guid projectId)
    {
        _ = projectId; //unused

        var items = Roles.ProjectRoles.Select(x => new Role
        {
            RoleId = x.RoleId,
            RoleName = x.RoleName,
            Description = ""
        });

        return Task.FromResult(items.ToArray());
    }

}