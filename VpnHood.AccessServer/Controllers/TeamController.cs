using GrayMint.Authorization.RoleManagement.TeamControllers.Controllers;
using GrayMint.Authorization.RoleManagement.TeamControllers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Authorize]
[ApiController]
public class TeamController : TeamControllerBase
{
    private readonly ProjectService _projectService;

    public TeamController(
        TeamService roleService, 
        ProjectService projectService) 
        : base(roleService)
    {
        _projectService = projectService;
    }


    [Authorize]
    [HttpGet("users/current/projects")]
    public async Task<IEnumerable<Project>> ListCurrentUserProjects()
    {
        var resourceIds = await ListCurrentUserResources();

        var projectIds = resourceIds
            .Where(x => x != GetRootResourceId())
        .Select(Guid.Parse);

        var ret = await _projectService.List(projectIds);
        return ret;
    }

}

