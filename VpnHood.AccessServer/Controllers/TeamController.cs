using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleUserControllers.Controllers;
using GrayMint.Common.AspNetCore.SimpleUserControllers.Services;
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
public class TeamController : TeamControllerBase<Project, Guid, User, UserRole, Role>
{
    private readonly ProjectService _projectService;

    public TeamController(RoleService roleService, ProjectService projectService) : base(roleService)
    {
        _projectService = projectService;
    }

    protected override User ToDto(GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.User user)
    {
        return user.ToDto();
    }

    protected override Role ToDto(GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.Role role)
    {
        return role.ToDto();
    }

    protected override UserRole ToDto(GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.UserRole userRole)
    {
        return userRole.ToDto();
    }

    protected override string ToResourceId(Guid appId)
    {
        return appId == Guid.Empty ? "*" : appId.ToString();
    }

    protected override Task<IEnumerable<Project>> GetResources(IEnumerable<string> resourceIds)
    {
        var projectIds = resourceIds.Except(new[] { "*" }).Select(Guid.Parse);
        return _projectService.List(projectIds);
    }
}

