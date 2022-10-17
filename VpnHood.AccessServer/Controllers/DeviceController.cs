﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/devices")]
public class DeviceController : SuperController<DeviceController>
{
    public DeviceController(ILogger<DeviceController> logger, VhContext vhContext, MultilevelAuthRepo multilevelAuthRepo) 
        : base(logger, vhContext, multilevelAuthRepo)
    {
    }

    [HttpGet("{deviceId:guid}")]
    public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        if (usageStartTime != null)
        {
            var ret = await List(projectId, deviceId: deviceId, usageStartTime: usageStartTime, usageEndTime: usageEndTime);
            return ret.Single();
        }
        else
        {
            await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

            var res = await VhContext.Devices
                .SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);

            var ret = new DeviceData { Device = res };
            return ret;
        }
    }

    [HttpGet("find-by-client")]
    public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var ret = await VhContext.Devices
            .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

        return ret;
    }

    [HttpPatch("{deviceId}")]
    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.IpLockWrite);

        var device = await VhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
        if (updateParams.IsLocked != null) device.LockedTime = updateParams.IsLocked && device.LockedTime == null ? DateTime.UtcNow : null;

        var res = VhContext.Devices.Update(device);
        await VhContext.SaveChangesAsync();

        return res.Entity;
    }

    [HttpGet]
    public async Task<DeviceData[]> List(Guid projectId, string? search = null,
        Guid? deviceId = null, DateTime? usageStartTime = null, DateTime? usageEndTime = null, int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        usageEndTime ??= DateTime.UtcNow;

        var usages =
            from accessUsage in VhContext.AccessUsages
            where accessUsage.ProjectId == projectId && 
                  accessUsage.CreatedTime >= usageStartTime && accessUsage.CreatedTime <= usageEndTime &&
                  (deviceId == null || accessUsage.DeviceId == deviceId)
            group accessUsage by new { accessUsage.DeviceId } into g
            select new
            {
                DeviceId = (Guid?)g.Key.DeviceId,
                SentTraffic = g.Sum(x => x.SentTraffic),
                ReceivedTraffic = g.Sum(x => x.ReceivedTraffic),
                LastUsedTime = g.Max(x => x.CreatedTime)
            };

        var query =
            from device in VhContext.Devices
            join usage in usages on device.DeviceId equals usage.DeviceId into grouping
            from usage in grouping.DefaultIfEmpty()
            where device.ProjectId == projectId &&
                  (deviceId == null || device.DeviceId == deviceId) &&
                  (string.IsNullOrEmpty(search) ||
                   device.DeviceId.ToString().StartsWith(search) ||
                   device.IpAddress!.StartsWith(search) ||
                   device.ClientId.ToString().StartsWith(search))
            orderby device.CreatedTime descending
            select new DeviceData
            {
                Device = device,
                Usage = usage.DeviceId != null ? new TrafficUsage
                {
                    LastUsedTime = usage.LastUsedTime,
                    ReceivedTraffic = usage.ReceivedTraffic,
                    SentTraffic = usage.SentTraffic
                } : null
            };

        var res = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return res;
    }
}