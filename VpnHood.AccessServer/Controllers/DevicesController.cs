using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Devices;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Report.Views;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId}/devices")]
[Authorize]
public class DevicesController(
    VhContext vhContext,
    ReportUsageService reportUsageService,
    DevicesService devicesService,
    SubscriptionService subscriptionService)
    : ControllerBase
{
    [HttpGet("{deviceId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageBeginTime = null,
        DateTime? usageEndTime = null)
    {
        // find the device
        await using var trans = await vhContext.WithNoLockTransaction();
        var deviceModel = await vhContext.Devices
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);

        var ret = new DeviceData { Device = deviceModel.ToDto() };
        if (usageBeginTime != null) {
            var usages = await List(projectId, deviceId: deviceId, usageBeginTime: usageBeginTime.Value,
                usageEndTime: usageEndTime);
            ret.Usage = usages.SingleOrDefault(x => x.Device.DeviceId == deviceModel.DeviceId)?.Usage ??
                        new TrafficUsage();
        }

        return ret;
    }

    [HttpGet("clientId:{clientId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<Device> GetByClientId(Guid projectId, Guid clientId)
    {
        return devicesService.GetByClientId(projectId, clientId);
    }

    [HttpPatch("{deviceId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        return devicesService.Update(projectId, deviceId, updateParams);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<DeviceData[]> List(Guid projectId,
        Guid? deviceId = null, DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        // get list of devices
        await using var trans = await vhContext.WithNoLockTransaction();
        var query = vhContext.Devices
            .Where(device =>
                device.ProjectId == projectId &&
                (deviceId == null || device.DeviceId == deviceId))
            .OrderByDescending(device => device.LastUsedTime)
            .Select(device => new DeviceData {
                Device = device.ToDto()
            });

        var results = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        // fill usage if requested
        if (usageBeginTime != null) {
            var deviceIds = results.Select(x => x.Device.DeviceId).ToArray();
            var usages = await reportUsageService.GetDevicesUsage(projectId, deviceIds,
                null, null, usageBeginTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.Device.DeviceId, out var usage))
                    result.Usage = usage;
        }

        return results;
    }

    [HttpGet("usages")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<DeviceData[]> ListUsages(Guid projectId,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var deviceDatas = await devicesService.ListUsages(projectId, accessTokenId, serverFarmId, 
            usageBeginTime, usageEndTime, recordIndex, recordCount);

        return deviceDatas.ToArray();
    }
}