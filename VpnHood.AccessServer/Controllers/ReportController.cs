﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Report.Views;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/report")]
public class ReportController(
    ReportService reportService, 
    SubscriptionService subscriptionService,
    ReportUsageService reportUsageService)
    : ControllerBase
{
    [HttpGet("usage")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverFarmId = null, Guid? serverId = null)
    {
        return reportService.GetUsage(projectId: projectId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime,
            serverFarmId: serverFarmId, serverId: serverId);
    }

    [HttpGet("status-history")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerStatusHistory[]> GetStatusHistory(Guid projectId,
        DateTime? usageBeginTime, DateTime? usageEndTime = null, Guid? serverFarmId = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var ret = await reportUsageService.GetServersStatusHistory(projectId, usageBeginTime.Value, usageEndTime, serverId);
        return ret;
    }
}