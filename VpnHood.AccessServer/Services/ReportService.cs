﻿using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Report.Views;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class ReportService(
    ReportUsageService reportUsageService,
    SubscriptionService subscriptionService,
    VhRepo vhRepo)
{
    public async Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
    Guid? serverFarmId = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);

        // validate serverFarmId and serverId because report service does not check permissions
        if (serverFarmId != null) _ = await vhRepo.ServerFarmGet(projectId, serverFarmId.Value);
        if (serverId != null) _ = await vhRepo.ServerGet(projectId, serverId.Value);

        var usage = await reportUsageService.GetUsage(projectId: projectId, 
            usageBeginTime: usageBeginTime.Value, usageEndTime: usageEndTime,
            serverFarmId: serverFarmId, serverId: serverId);
        return usage;
    }

    public async Task<ServerStatusHistory[]> GetServerStatusHistory(Guid projectId,
        DateTime? usageBeginTime, DateTime? usageEndTime = null, Guid? serverFarmId = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        // validate serverFarmId and serverId because report service does not check permissions
        if (serverFarmId != null) _ = await vhRepo.ServerFarmGet(projectId, serverFarmId.Value);
        if (serverId != null) _ = await vhRepo.ServerGet(projectId, serverId.Value);

        var ret = await reportUsageService.GetServersStatusHistory(projectId: projectId,
            usageBeginTime: usageBeginTime.Value, usageEndTime: usageEndTime,
            serverFarmId: serverFarmId, serverId: serverId);

        return ret;
    }

}