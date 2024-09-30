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
    public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
    {
        var deviceModel = await vhContext.Devices
            .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

        return deviceModel.ToDto();
    }

    [HttpPatch("{deviceId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        var deviceModel = await vhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
        if (updateParams.IsLocked != null)
            deviceModel.LockedTime = updateParams.IsLocked && deviceModel.LockedTime == null ? DateTime.UtcNow : null;

        deviceModel = vhContext.Devices.Update(deviceModel).Entity;
        await vhContext.SaveChangesAsync();
        return deviceModel.ToDto();
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
                (device.ProjectId == projectId) &&
                (deviceId == null || device.DeviceId == deviceId))
            .OrderByDescending(device => device.UsedTime)
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

        var usagesDictionary = await reportUsageService.GetDevicesUsage(projectId,
            accessTokenId: accessTokenId, serverFarmId: serverFarmId, usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);

        var usages = usagesDictionary
            .OrderByDescending(x => x.Value.SentTraffic + x.Value.ReceivedTraffic)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new {
                DeviceId = x.Key,
                Traffic = x.Value
            })
            .ToArray();

        // get all devices accessed during usage time
        await using var trans = await vhContext.WithNoLockTransaction();
        var devices = await vhContext.Devices
            .Where(model=>model.ProjectId == projectId)
            .Where(device => device.UsedTime >= usageBeginTime && device.UsedTime <= usageEndTime)
            .ToDictionaryAsync(device => device.DeviceId, device => device);

        // create DeviceData
        var deviceDatas = new List<DeviceData>();
        foreach (var usage in usages) {
            if (devices.TryGetValue(usage.DeviceId, out var device))
                deviceDatas.Add(new DeviceData {
                    Device = device.ToDto(),
                    Usage = usage.Traffic
                });
        }

        return deviceDatas.ToArray();
    }
}