using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using GrayMint.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using Role = VpnHood.AccessServer.Dtos.UserDtos.Role;
using UserRole = VpnHood.AccessServer.Dtos.UserDtos.UserRole;

namespace VpnHood.AccessServer.Controllers;



[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/team")]
public class TeamController : ControllerBase
{
    private readonly SimpleRoleProvider _simpleRoleProvider;
    private readonly SimpleRoleAuthService _simpleRoleAuthService;
    private readonly SimpleUserProvider _simpleUserProvider;
    private readonly SubscriptionService _subscriptionService;

    public TeamController(
        SimpleRoleProvider simpleRoleProvider,
        SimpleUserProvider simpleUserProvider,
        SubscriptionService subscriptionService,
        SimpleRoleAuthService simpleRoleAuthService)
    {
        _simpleRoleProvider = simpleRoleProvider;
        _simpleUserProvider = simpleUserProvider;
        _subscriptionService = subscriptionService;
        _simpleRoleAuthService = simpleRoleAuthService;
    }


    [HttpPost("users")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task<UserRole> AddUser(Guid projectId, TeamAddUserParam addParam)
    {
        await _subscriptionService.VerifyAddUser(projectId);
        await ValidateWriteAccessToRole(projectId, addParam.RoleId);

        // make sure roleId is a project role
        if (Roles.ProjectRoles.All(x => x.RoleId != addParam.RoleId))
            throw new NotExistsException($"Could not find the {nameof(addParam.RoleId)}.");

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

    [HttpGet("users/{userId:guid}")]
    [AuthorizePermission(Permissions.TeamRead)]
    public async Task<UserRole> GetUser(Guid projectId, Guid userId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(userId: userId, appId: projectId.ToString());
        return userRoles.Single(x => x.User.UserId == userId).ToDto();
    }


    [HttpPost("users/{userId:guid}")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task<UserRole> UpdateUser(Guid projectId, Guid userId, TeamUpdateUserParam updateParam)
    {
        var userRole = await GetUser(projectId, userId);

        if (updateParam.RoleId != null)
        {
            await ValidateWriteAccessToRole(projectId, updateParam.RoleId);
            await ValidateWriteAccessToRole(projectId, userRole.Role.RoleId);

            // can not change its own owner role
            if (userRole.Role.RoleId == Roles.ProjectOwner.RoleId && userId == UserService.GetUserId(User))
                throw new InvalidOperationException("You are an owner and can not remove yourself. Ask other owners or delete the project.");

            //remove the old roles
            await _simpleRoleProvider.RemoveUser(userRole.Role.RoleId, userId, projectId.ToString());

            // add to new role
            await _simpleRoleProvider.AddUser<UserExData>(updateParam.RoleId, userId, projectId.ToString());
        }

        userRole = await GetUser(projectId, userId);
        return userRole;
    }


    [HttpDelete("users/userId")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task RemoveUser(Guid projectId, Guid userId)
    {
        var userRole = await GetUser(projectId, userId);
        await ValidateWriteAccessToRole(projectId, userRole.Role.RoleId);

        if (userRole.Role.RoleId == Roles.ProjectOwner.RoleId)
        {
            var authorizeResult = await _simpleRoleAuthService.AuthorizePermissionAsync(User, projectId.ToString(), Permissions.TeamWriteOwner);
            if (!authorizeResult.Succeeded)
                throw new UnauthorizedAccessException();

            if (userRole.Role.RoleId == Roles.ProjectOwner.RoleId && userId == UserService.GetUserId(User))
                throw new InvalidOperationException("You are an owner and can not remove yourself. Ask other owners or delete the project.");
        }

        // remove the user
        await _simpleRoleProvider.RemoveUser(userRole.Role.RoleId, userId, projectId.ToString());

        // remove user from repo if he is not in any role
        var allUserRoles = await _simpleRoleProvider.GetUserRoles(userId);
        if (!allUserRoles.Any())
            await _simpleUserProvider.Remove(userId);
    }

    [HttpGet("users")]
    [AuthorizePermission(Permissions.TeamRead)]
    public async Task<UserRole[]> ListUsers(Guid projectId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles<UserExData>(appId: projectId.ToString());
        return userRoles.Select(x => x.ToDto()).ToArray();
    }

    [HttpGet("roles")]
    [AuthorizePermission(Permissions.TeamRead)]
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

    private async Task ValidateWriteAccessToRole(Guid projectId, Guid roleId)
    {
        if (Roles.ProjectRoles.All(x => x.RoleId != roleId))
            throw new NotExistsException("The roleId is not a Project Role.");

        if (roleId == Roles.ProjectOwner.RoleId)
        {
            var authorizeResult = await _simpleRoleAuthService.AuthorizePermissionAsync(User, projectId.ToString(), Permissions.TeamWriteOwner);
            if (!authorizeResult.Succeeded)
                throw new UnauthorizedAccessException();
        }
    }
}
