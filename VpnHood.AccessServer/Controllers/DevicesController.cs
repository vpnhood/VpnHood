using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/devices")]
public class DevicesController : SuperController<DevicesController>
{
    private readonly UsageReportService _usageReportService;

    public DevicesController(ILogger<DevicesController> logger,
        VhContext vhContext,
        UsageReportService usageReportService,
        MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _usageReportService = usageReportService;
    }

    [HttpGet("{deviceId:guid}")]
    public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        // find the device
        await using var trans = await VhContext.WithNoLockTransaction();
        var deviceModel = await VhContext.Devices
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);

        var ret = new DeviceData { Device = deviceModel.ToDto() };
        if (usageStartTime != null)
        {
            var usages = await List(projectId, deviceId: deviceId, usageStartTime: usageStartTime.Value, usageEndTime: usageEndTime);
            ret.Usage = usages.SingleOrDefault(x => x.Device.DeviceId == deviceModel.DeviceId)?.Usage ?? new TrafficUsage();
        }

        return ret;
    }

    [HttpGet("clientId:{clientId:guid}")]
    public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var deviceModel = await VhContext.Devices
            .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

        return deviceModel.ToDto();
    }

    [HttpPatch("{deviceId:guid}")]
    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.IpLockWrite);

        var deviceModel = await VhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
        if (updateParams.IsLocked != null) deviceModel.LockedTime = updateParams.IsLocked && deviceModel.LockedTime == null ? DateTime.UtcNow : null;

        deviceModel = VhContext.Devices.Update(deviceModel).Entity;
        await VhContext.SaveChangesAsync();

        return deviceModel.ToDto();
    }

    [HttpGet]
    public async Task<DeviceData[]> List(Guid projectId,
        Guid? deviceId = null, DateTime? usageStartTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);

        // get list of devices
        await using var trans = await VhContext.WithNoLockTransaction();
        var query = VhContext.Devices
            .Where(device =>
                (device.ProjectId == projectId) &&
                (deviceId == null || device.DeviceId == deviceId))
            .OrderByDescending(device => device.ModifiedTime)
            .Select(device => new DeviceData
            {
                Device = device.ToDto(),
            });

        var results = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        // fill usage if requested
        if (usageStartTime != null)
        {
            var deviceIds = results.Select(x => x.Device.DeviceId).ToArray();
            var usages = await _usageReportService.GetDevicesUsage(projectId, deviceIds,
                null, null, usageStartTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.Device.DeviceId, out var usage))
                    result.Usage = usage;
        }

        return results;
    }

    [HttpGet("usages")]
    public async Task<DeviceData[]> ListUsages(Guid projectId,
        Guid? accessTokenId = null, Guid? accessPointGroupId = null,
        DateTime? usageStartTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);

        var usagesDictionary = await _usageReportService.GetDevicesUsage(projectId,
            accessTokenId: accessTokenId, accessPointGroupId: accessPointGroupId);

        var usages = usagesDictionary
            .OrderByDescending(x => x.Value.SentTraffic + x.Value.ReceivedTraffic)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new
            {
                DeviceId = x.Key,
                TrafficUsage = x.Value,
            })
            .ToArray();

        // get all devices accessed during usage time
        await using var trans = await VhContext.WithNoLockTransaction();
        var devices = await VhContext.Devices
            .Where(device => device.ModifiedTime >= usageStartTime && device.ModifiedTime <= usageEndTime)
            .ToDictionaryAsync(device => device.DeviceId, device => device);

        // create DeviceData
        var deviceDatas = new List<DeviceData>();
        foreach (var usage in usages)
        {
            if (devices.TryGetValue(usage.DeviceId, out var device))
                deviceDatas.Add(new DeviceData
                {
                    Device = device.ToDto(),
                    Usage = usage.TrafficUsage
                });
        }

        return deviceDatas.ToArray();
    }
}