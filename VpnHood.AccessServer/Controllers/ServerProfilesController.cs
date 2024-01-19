using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using Microsoft.AspNetCore.Authorization;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/server-profiles")]
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
    public Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        return _serverProfileService.Create(projectId, createParams);
    }

    [HttpGet("{serverProfileId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerProfileData> Get(Guid projectId, Guid serverProfileId, bool includeSummary = false)
    {
        var dtos = await _serverProfileService.ListWithSummary(projectId, serverProfileId: serverProfileId, includeSummary: includeSummary);
        return dtos.Single();
    }

    [HttpPatch("{serverProfileId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        return _serverProfileService.Update(projectId, serverProfileId, updateParams);
    }

    [HttpDelete("{serverProfileId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task Delete(Guid projectId, Guid serverProfileId)
    {
        return _serverProfileService.Delete(projectId, serverProfileId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerProfileData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return _serverProfileService.ListWithSummary(projectId, search, includeSummary: includeSummary, 
            recordIndex: recordIndex, recordCount: recordCount);
    }
}