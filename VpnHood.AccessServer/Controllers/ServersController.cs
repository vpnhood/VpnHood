using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Authorization.RoleManagement.RoleAuthorizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using ServerStatusHistory = VpnHood.AccessServer.Dtos.ServerStatusHistory;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/servers")]
public class ServersController : ControllerBase
{
    private readonly UsageReportService _usageReportService;
    private readonly ServerService _serverService;
    private readonly SubscriptionService _subscriptionService;

    public ServersController(
        UsageReportService usageReportService,
        ServerService serverService,
        SubscriptionService subscriptionService)
    {
        _usageReportService = usageReportService;
        _serverService = serverService;
        _subscriptionService = subscriptionService;
    }

    [HttpPost]
    [AuthorizePermission(Permissions.ServerWrite)]
    public Task<VpnServer> Create(Guid projectId, ServerCreateParams createParams)
    {
        return _serverService.Create(projectId, createParams);
    }

    [HttpPatch("{serverId:guid}")]
    [AuthorizePermission(Permissions.ServerWrite)]
    public Task<VpnServer> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        return _serverService.Update(projectId, serverId, updateParams);
    }

    [HttpGet("{serverId:guid}")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<ServerData> Get(Guid projectId, Guid serverId)
    {
        var list = await _serverService.List(projectId, serverId: serverId);
        return list.Single();
    }

    [HttpDelete("{serverId:guid}")]
    [AuthorizePermission(Permissions.ServerWrite)]
    public Task Delete(Guid projectId, Guid serverId)
    {
        return _serverService.Delete(projectId, serverId);
    }

    [HttpGet]
    [AuthorizePermission(Permissions.ProjectRead)]
    public Task<ServerData[]> List(Guid projectId, string? search = null, Guid? serverId = null, Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = 1000)
    {
        return _serverService.List(projectId, search: search, serverId: serverId, serverFarmId: serverFarmId, recordIndex, recordCount);
    }

    [HttpPost("{serverId:guid}/reconfigure")]
    [AuthorizePermission(Permissions.ServerInstall)]
    public Task Reconfigure(Guid projectId, Guid serverId)
    {
        return _serverService.Reconfigure(projectId, serverId);
    }
    
    [HttpPost("{serverId:guid}/install-by-ssh-user-password")]
    [AuthorizePermission(Permissions.ServerInstall)]
    public Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {
        return _serverService.InstallBySshUserPassword(projectId, serverId, installParams);
    }

    [HttpPost("{serverId:guid}/install-by-ssh-user-key")]
    [AuthorizePermission(Permissions.ServerInstall)]
    public Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        return _serverService.InstallBySshUserKey(projectId, serverId, installParams);
    }
    
    [HttpGet("{serverId:guid}/install/manual")]
    [AuthorizePermission(Permissions.ServerInstall)]
    public Task<ServerInstallManual> GetInstallManual(Guid projectId, Guid serverId)
    {
        return _serverService.GetInstallManual(projectId, serverId);
    }

    [HttpGet("status-summary")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public Task<ServersStatusSummary> GetStatusSummary(Guid projectId, Guid? serverFarmId = null)
    {
        return _serverService.GetStatusSummary(projectId, serverFarmId);
    }

    [HttpGet("status-history")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<ServerStatusHistory[]> GetStatusHistory(Guid projectId,
        DateTime? usageBeginTime, DateTime? usageEndTime = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await _subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var ret = await _usageReportService.GetServersStatusHistory(projectId, usageBeginTime.Value, usageEndTime, serverId);
        return ret;
    }
}