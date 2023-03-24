using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using GrayMint.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/roles")]
public class RolesController
{
    private readonly SimpleRoleProvider _simpleRoleProvider;
    private readonly SimpleUserProvider _simpleUserProvider;
    public RolesController(
        SimpleRoleProvider simpleRoleProvider, 
        SimpleUserProvider simpleUserProvider)
    {
        _simpleRoleProvider = simpleRoleProvider;
        _simpleUserProvider = simpleUserProvider;
    }

    [HttpPost("{roleId:guid}/users/email:{email}")]
    [AuthorizePermission(Permissions.RoleWrite)]
    public async Task AddUserByEmail(Guid projectId, Guid roleId, string email)
    {
        // create user if not found
        var user = await _simpleUserProvider.FindByEmail(email);
        user ??= await _simpleUserProvider.Create(new UserCreateRequest(email));
        await _simpleRoleProvider.AddUser(roleId, user.UserId, projectId.ToString());
    }

    [HttpPost("{roleId:guid}/users")]
    [AuthorizePermission(Permissions.RoleRead)]
    public async Task<User[]> GetUsers(Guid projectId, Guid roleId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles(roleId: roleId, appId: projectId.ToString());
        return userRoles.Select(x => x.User).ToArray();
    }

    [HttpDelete("{roleId:guid}/users/userId")]
    [AuthorizePermission(Permissions.RoleWrite)]
    public async Task RemoveUser(Guid projectId, Guid roleId, Guid userId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles(roleId: roleId, userId: userId, appId: projectId.ToString());
        if (!userRoles.Any())
            throw new NotExistsException();

        if (userRoles.Any(x => x.Role.RoleName == Roles.ProjectOwner.RoleName))
            throw new InvalidOperationException("You can not remove yourself from the owners. Ask the other owner or delete the project.");

        await _simpleRoleProvider.RemoveUser(roleId, userId, projectId.ToString());
    }
}