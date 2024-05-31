using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Servers;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/servers")]
public class ServersController(
    ServerService serverService)
    : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ServerWrite)]
    public Task<ServerData> Create(Guid projectId, ServerCreateParams createParams)
    {
        return serverService.Create(projectId, createParams);
    }

    [HttpPatch("{serverId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerWrite)]
    public Task<ServerData> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        return serverService.Update(projectId, serverId, updateParams);
    }

    [HttpGet("{serverId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerData> Get(Guid projectId, Guid serverId)
    {
        return serverService.Get(projectId, serverId: serverId);
    }

    [HttpDelete("{serverId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerWrite)]
    public Task Delete(Guid projectId, Guid serverId)
    {
        return serverService.Delete(projectId, serverId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerData[]> List(Guid projectId, string? search = null, Guid? serverId = null, Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = 1000)
    {
        return serverService.List(projectId, search: search, serverId: serverId, serverFarmId: serverFarmId, recordIndex, recordCount);
    }

    [HttpPost("{serverId:guid}/reconfigure")]
    [AuthorizeProjectPermission(Permissions.ServerInstall)]
    public Task Reconfigure(Guid projectId, Guid serverId)
    {
        return serverService.Reconfigure(projectId, serverId);
    }
    
    [HttpPost("{serverId}/install-by-ssh-user-password")]
    [AuthorizeProjectPermission(Permissions.ServerInstall)]
    public Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {
        return serverService.InstallBySshUserPassword(projectId, serverId, installParams);
    }

    [HttpPost("{serverId}/install-by-ssh-user-key")]
    [AuthorizeProjectPermission(Permissions.ServerInstall)]
    public Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        return serverService.InstallBySshUserKey(projectId, serverId, installParams);
    }
    
    [HttpGet("{serverId}/install/manual")]
    [AuthorizeProjectPermission(Permissions.ServerInstall)]
    public Task<ServerInstallManual> GetInstallManual(Guid projectId, Guid serverId)
    {
        return serverService.GetInstallManual(projectId, serverId);
    }

    [HttpGet("status-summary")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServersStatusSummary> GetStatusSummary(Guid projectId, Guid? serverFarmId = null)
    {
        return serverService.GetStatusSummary(projectId, serverFarmId);
    }
}