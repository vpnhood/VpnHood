using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Authorization.RoleManagement.TeamControllers.Controllers;
using GrayMint.Authorization.RoleManagement.TeamControllers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Services;
using Role = VpnHood.AccessServer.Dtos.Role;
using UserRole = VpnHood.AccessServer.Dtos.UserRole;

namespace VpnHood.AccessServer.Controllers;

[Authorize]
[ApiController]
public class TeamController : TeamControllerBase<string, User, UserRole, Role>
{
    private readonly ProjectService _projectService;

    public TeamController(
        TeamService roleService, 
        ProjectService projectService) 
        : base(roleService)
    {
        _projectService = projectService;
    }

    protected override string RootResourceId => "*";


    protected override User ToDto(GrayMint.Authorization.RoleManagement.TeamControllers.Dtos.User user)
    {
        return user.ToDto();
    }

    protected override Role ToDto(GrayMint.Authorization.RoleManagement.TeamControllers.Dtos.Role role)
    {
        return role.ToDto();
    }

    protected override UserRole ToDto(GrayMint.Authorization.RoleManagement.TeamControllers.Dtos.UserRole userRole)
    {
        return userRole.ToDto();
    }


    [Authorize]
    [HttpGet("users/current/projects")]
    public async Task<IEnumerable<Project>> ListCurrentUserProjects()
    {
        var resourceIds = await ListCurrentUserResources();

        var projectIds = resourceIds
            .Where(x => x != RootResourceId.ToString())
        .Select(Guid.Parse);

        var ret = await _projectService.List(projectIds);
        return ret;
    }

}

