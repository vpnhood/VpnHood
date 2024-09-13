using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.Abstractions.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Projects;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects")]
public class ProjectsController(
    ProjectService projectService,
    IAuthorizationProvider authorizationProvider)
    : ControllerBase
{
    [HttpPost]
    [Authorize] //all authenticated user can create projects
    public async Task<Project> Create()
    {
        var userId = await authorizationProvider.GetUserId(User);
        if (userId == null) throw new UnregisteredUserException();
        return await projectService.Create(userId);
    }

    [HttpGet("{projectId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<Project> Get(Guid projectId)
    {
        return projectService.Get(projectId);
    }

    [HttpPatch("{projectId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        return projectService.Update(projectId, updateParams);
    }

    [HttpDelete("{projectId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectDelete)]
    public Task Delete(Guid projectId)
    {
        return projectService.Delete(projectId);
    }


    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectList)]
    public Task<Project[]> List(string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        return projectService.List(search, recordIndex, recordCount);
    }
}