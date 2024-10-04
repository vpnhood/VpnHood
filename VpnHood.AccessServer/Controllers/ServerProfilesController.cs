using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.ServerProfiles;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/server-profiles")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class ServerProfilesController(ServerProfileService serverProfileService)
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        return serverProfileService.Create(projectId, createParams);
    }

    [HttpGet("{serverProfileId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerProfileData> Get(Guid projectId, Guid serverProfileId, bool includeSummary = false)
    {
        var dtos = await serverProfileService.ListWithSummary(projectId, serverProfileId: serverProfileId,
            includeSummary: includeSummary);
        return dtos.Single();
    }

    [HttpPatch("{serverProfileId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        return serverProfileService.Update(projectId, serverProfileId, updateParams);
    }

    [HttpDelete("{serverProfileId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task Delete(Guid projectId, Guid serverProfileId)
    {
        return serverProfileService.Delete(projectId, serverProfileId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerProfileData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return serverProfileService.ListWithSummary(projectId, search, includeSummary: includeSummary,
            recordIndex: recordIndex, recordCount: recordCount);
    }
}