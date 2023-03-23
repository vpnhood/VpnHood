using System;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly ProjectService _projectService;

    public ProjectsController(
        ProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpPost]
    [AuthorizePermission(Permission.ProjectCreate)]
    public Task<Project> Create()
    {
        var userId = UserService.GetUserId(User);
        return _projectService.Create(userId);
    }

    [HttpGet("{projectId:guid}")]
    [AuthorizePermission(Permission.ProjectRead)]
    public Task<Project> Get(Guid projectId)
    {
        return _projectService.Get(projectId);
    }

    [HttpPatch("{projectId:guid}")]
    [AuthorizePermission(Permission.ProjectWrite)]
    public Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        return _projectService.Update(projectId, updateParams);
    }

    [HttpGet]
    [AuthorizePermission(Permission.ProjectList)]
    public Task<Project[]> List(string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        return _projectService.List(search, recordIndex, recordCount);
    }

    [HttpGet("usage")]
    [AuthorizePermission(Permission.ProjectRead)]
    public  Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverFarmId = null, Guid? serverId = null)
    {
        return _projectService.GetUsage(projectId, usageBeginTime, usageEndTime, serverFarmId, serverId);
    }
}