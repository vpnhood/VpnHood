using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly UsageReportService _usageReportService;
    private readonly VhContext _vhContext;
    private readonly SubscriptionService _subscriptionService;

    public DevicesController(
        VhContext vhContext,
        UsageReportService usageReportService, 
        SubscriptionService subscriptionService)
    {
        _vhContext = vhContext;
        _usageReportService = usageReportService;
        _subscriptionService = subscriptionService;
    }

    [HttpGet("{deviceId:guid}")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageBeginTime = null, DateTime? usageEndTime = null)
    {
        // find the device
        await using var trans = await _vhContext.WithNoLockTransaction();
        var deviceModel = await _vhContext.Devices
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);

        var ret = new DeviceData { Device = deviceModel.ToDto() };
        if (usageBeginTime != null)
        {
            var usages = await List(projectId, deviceId: deviceId, usageBeginTime: usageBeginTime.Value, usageEndTime: usageEndTime);
            ret.Usage = usages.SingleOrDefault(x => x.Device.DeviceId == deviceModel.DeviceId)?.Usage ?? new TrafficUsage();
        }

        return ret;
    }

    [HttpGet("clientId:{clientId:guid}")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
    {
        var deviceModel = await _vhContext.Devices
            .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

        return deviceModel.ToDto();
    }

    [HttpPatch("{deviceId:guid}")]
    [AuthorizePermission(Permissions.ProjectWrite)]
    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        var deviceModel = await _vhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
        if (updateParams.IsLocked != null) deviceModel.LockedTime = updateParams.IsLocked && deviceModel.LockedTime == null ? DateTime.UtcNow : null;

        deviceModel = _vhContext.Devices.Update(deviceModel).Entity;
        await _vhContext.SaveChangesAsync();

        return deviceModel.ToDto();
    }

    [HttpGet]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<DeviceData[]> List(Guid projectId,
        Guid? deviceId = null, DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await _subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        // get list of devices
        await using var trans = await _vhContext.WithNoLockTransaction();
        var query = _vhContext.Devices
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
        if (usageBeginTime != null)
        {
            var deviceIds = results.Select(x => x.Device.DeviceId).ToArray();
            var usages = await _usageReportService.GetDevicesUsage(projectId, deviceIds,
                null, null, usageBeginTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.Device.DeviceId, out var usage))
                    result.Usage = usage;
        }

        return results;
    }

    [HttpGet("usages")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<DeviceData[]> ListUsages(Guid projectId,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 100)
    {
        await _subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var usagesDictionary = await _usageReportService.GetDevicesUsage(projectId,
            accessTokenId: accessTokenId, serverFarmId: serverFarmId);

        var usages = usagesDictionary
            .OrderByDescending(x => x.Value.SentTraffic + x.Value.ReceivedTraffic)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new
            {
                DeviceId = x.Key,
                Traffic = x.Value,
            })
            .ToArray();

        // get all devices accessed during usage time
        await using var trans = await _vhContext.WithNoLockTransaction();
        var devices = await _vhContext.Devices
            .Where(device => device.ModifiedTime >= usageBeginTime && device.ModifiedTime <= usageEndTime)
            .ToDictionaryAsync(device => device.DeviceId, device => device);

        // create DeviceData
        var deviceDatas = new List<DeviceData>();
        foreach (var usage in usages)
        {
            if (devices.TryGetValue(usage.DeviceId, out var device))
                deviceDatas.Add(new DeviceData
                {
                    Device = device.ToDto(),
                    Usage = usage.Traffic
                });
        }

        return deviceDatas.ToArray();
    }
}