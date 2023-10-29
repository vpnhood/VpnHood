using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;
using VpnHood.AccessServer.Security;
using System.Linq;
using VpnHood.AccessServer.Services;
using Microsoft.AspNetCore.Authorization;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-profiles")]
public class ServerProfilesController 
{
    private readonly ServerProfileService _serverProfileService;

    public ServerProfilesController(
        ServerProfileService serverProfileService)
    {
        _serverProfileService = serverProfileService;
    }

    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        return await _serverProfileService.Create(projectId, createParams);
    }

    [HttpGet("{serverProfileId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerProfileData> Get(Guid projectId, Guid serverProfileId, bool includeSummary = false)
    {
        var dtos = await _serverProfileService.ListWithSummary(projectId, serverProfileId: serverProfileId, includeSummary: includeSummary);
        return dtos.Single();
    }

    [HttpPatch("{serverProfileId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        return await _serverProfileService.Update(projectId, serverProfileId, updateParams);
    }

    [HttpDelete("{serverProfileId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task Delete(Guid projectId, Guid serverProfileId)
    {
        await _serverProfileService.Delete(projectId, serverProfileId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerProfileData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return await _serverProfileService.ListWithSummary(projectId, search, includeSummary: includeSummary, 
            recordIndex: recordIndex, recordCount: recordCount);
    }
}