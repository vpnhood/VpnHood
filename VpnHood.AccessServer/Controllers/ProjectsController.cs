using System;
using System.Threading.Tasks;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.RoleManagement.RoleAuthorizations;
using GrayMint.Authorization.RoleManagement.TeamControllers.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectService _projectService;
    private readonly IAuthorizationProvider _authorizationProvider;

    public ProjectsController(
        ProjectService projectService, 
        IAuthorizationProvider authorizationProvider)
    {
        _projectService = projectService;
        _authorizationProvider = authorizationProvider;
    }

    [HttpPost]
    [Authorize] //all authenticated user can create projects
    public async Task<Project> Create()
    {
        var userId = await _authorizationProvider.GetUserId(User);
        if (userId == null) throw new UnregisteredUser();

        return await _projectService.Create(userId.Value);
    }

    [HttpGet("{projectId:guid}")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public Task<Project> Get(Guid projectId)
    {
        return _projectService.Get(projectId);
    }

    [HttpPatch("{projectId:guid}")]
    [AuthorizePermission(Permissions.ProjectWrite)]
    public Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        return _projectService.Update(projectId, updateParams);
    }

    [HttpPatch("{projectId:guid}/usage")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverFarmId = null, Guid? serverId = null)
    {
        return _projectService.GetUsage(projectId, usageBeginTime, usageEndTime, serverFarmId, serverId);
    }

    [HttpGet]
    [AuthorizePermission(Permissions.ProjectList)]
    public Task<Project[]> List(string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        return _projectService.List(search, recordIndex, recordCount);
    }
}