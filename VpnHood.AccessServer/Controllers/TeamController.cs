using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using Role = VpnHood.AccessServer.Dtos.Role;
using UserRole = VpnHood.AccessServer.Dtos.UserRole;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/team")]
public class TeamController : ControllerBase
{
    private readonly TeamService _teamService;
    private readonly SimpleRoleAuthService _simpleRoleAuthService;

    public TeamController(
        TeamService teamService, 
        SimpleRoleAuthService simpleRoleAuthService)
    {
        _teamService = teamService;
        _simpleRoleAuthService = simpleRoleAuthService;
    }

    [HttpPost("bots")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task<BotAuthorizationResult> CreateBot(Guid projectId, TeamAddBotParam addParam)
    {
        await ValidateWriteAccessToRole(projectId, addParam.RoleId);
        return await _teamService.CreateBot(projectId, addParam);
    }

    [HttpPost("bots/reset-authorization")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task<BotAuthorizationResult> ResetBotAuthorization(Guid projectId, Guid userId)
    {
        return await _teamService.ResetBotAuthorization(projectId, userId);
    }

    [HttpPost("users")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task<UserRole> AddUser(Guid projectId, TeamAddUserParam addParam)
    {
        await ValidateWriteAccessToRole(projectId, addParam.RoleId);
        return  await _teamService.AddUser(projectId, addParam);
    }

    [HttpGet("users/{userId:guid}")]
    [AuthorizePermission(Permissions.TeamRead)]
    public Task<UserRole> GetUser(Guid projectId, Guid userId)
    {
        return _teamService.GetUser(projectId, userId);
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
        }

        return await _teamService.UpdateUser(projectId, userId, updateParam);
    }


    [HttpDelete("users/userId")]
    [AuthorizePermission(Permissions.TeamWrite)]
    public async Task RemoveUser(Guid projectId, Guid userId)
    {
        var userRole = await GetUser(projectId, userId);
        await ValidateWriteAccessToRole(projectId, userRole.Role.RoleId);

        if (userRole.Role.RoleId == Roles.ProjectOwner.RoleId && userId == UserService.GetUserId(User))
            throw new InvalidOperationException("An owner can not remove himself. Ask other owners or delete the project.");

        await _teamService.RemoveUser(projectId, userId);
    }

    [HttpGet("users")]
    [AuthorizePermission(Permissions.TeamRead)]
    public Task<UserRole[]> ListUsers(Guid projectId)
    {
        return _teamService.ListUsers(projectId);
    }

    [HttpGet("roles")]
    [AuthorizePermission(Permissions.TeamRead)]
    public Task<Role[]> ListRoles(Guid projectId)
    {
        return _teamService.ListRoles(projectId);
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

