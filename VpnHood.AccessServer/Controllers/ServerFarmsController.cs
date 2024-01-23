using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/server-farms")]
public class ServerFarmsController(ServerFarmService serverFarmService) : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task<ServerFarm> Create(Guid projectId, ServerFarmCreateParams? createParams)
    {
        createParams ??= new ServerFarmCreateParams();
        return serverFarmService.Create(projectId, createParams);
    }

    [HttpPatch("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task<ServerFarmData> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        return serverFarmService.Update(projectId, serverFarmId, updateParams);
    }

    [HttpGet("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary = false, bool validateTokenUrl = false, 
        CancellationToken cancellationToken = default)
    {
        return serverFarmService.Get(projectId, serverFarmId: serverFarmId, 
            includeSummary: includeSummary, validateTokenUrl: validateTokenUrl, cancellationToken);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return includeSummary
            ? await serverFarmService.ListWithSummary(projectId, search, null, recordIndex, recordCount)
            : await serverFarmService.List(projectId, search, null, recordIndex, recordCount);
    }

    [HttpDelete("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task Delete(Guid projectId, Guid serverFarmId)
    {
        return serverFarmService.Delete(projectId, serverFarmId);
    }
}