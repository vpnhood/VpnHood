using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/devices")]
public class DeviceController : SuperController<DeviceController>
{
    private readonly IMemoryCache _memoryCache;
    private readonly VhReportContext _vhReportContext;

    public DeviceController(ILogger<DeviceController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService, IMemoryCache memoryCache, VhReportContext vhReportContext)
        : base(logger, vhContext, multilevelAuthService)
    {
        _memoryCache = memoryCache;
        _vhReportContext = vhReportContext;
    }

    [HttpGet("{deviceId:guid}")]
    public async Task<DeviceData> Get(Guid projectId, Guid deviceId, DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // find the device
        await using var trans = await VhContext.WithNoLockTransaction();
        var deviceModel = await VhContext.Devices
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);

        var ret = new DeviceData { Device = deviceModel.ToDto() };
        if (usageStartTime != null)
        {
            var usages = await List(projectId, deviceId: deviceId, usageStartTime: usageStartTime, usageEndTime: usageEndTime);
            ret.Usage = usages.SingleOrDefault(x => x.Device.DeviceId == deviceModel.DeviceId)?.Usage ?? new TrafficUsage();
        }

        return ret;
    }

    [HttpGet("find-by-client")]
    public async Task<Device> FindByClientId(Guid projectId, Guid clientId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var deviceModel = await VhContext.Devices
            .SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);

        return deviceModel.ToDto();
    }

    [HttpPatch("{deviceId}")]
    public async Task<Device> Update(Guid projectId, Guid deviceId, DeviceUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.IpLockWrite);

        var deviceModel = await VhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.DeviceId == deviceId);
        if (updateParams.IsLocked != null) deviceModel.LockedTime = updateParams.IsLocked && deviceModel.LockedTime == null ? DateTime.UtcNow : null;

        var res = VhContext.Devices.Update(deviceModel);
        await VhContext.SaveChangesAsync();

        return DeviceConverter.ToDto(deviceModel);
    }

    [HttpGet]
    public async Task<DeviceData[]> List(Guid projectId,
        Guid? deviceId = null, Guid? accessTokenId = null,
        DateTime? usageStartTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"accessToken_devices_{projectId}_{recordIndex}_{recordCount}", usageStartTime, usageEndTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out DeviceData[] cacheRes))
            return cacheRes;

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        // vhReportContext
        var usages = await
            _vhReportContext.AccessUsages
            .Where(accessUsage =>
                (accessUsage.ProjectId == projectId) &&
                (accessUsage.DeviceId == deviceId || deviceId == null) &&
                (accessUsage.AccessTokenId == accessTokenId || accessTokenId == null) &&
                (accessUsage.CreatedTime >= usageStartTime || usageStartTime != null) &&
                (accessUsage.CreatedTime <= usageEndTime || usageEndTime == null))
            .GroupBy(accessUsage => accessUsage.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                Usage = new TrafficUsage
                {
                    LastUsedTime = g.Max(x => x.CreatedTime),
                    SentTraffic = g.Sum(x => x.SentTraffic),
                    ReceivedTraffic = g.Sum(x => x.ReceivedTraffic)
                }
            })
            .OrderByDescending(x => x.Usage.LastUsedTime)
            .ToArrayAsync();

        // find devices 
        var deviceIds = usages.Select(x => x.DeviceId);
        var deviceModels = await VhContext.Devices
            .Where(device => device.ProjectId == projectId && deviceIds.Any(x => x == device.DeviceId))
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        // merge
        var res = deviceModels.Select(deviceModel =>
            new DeviceData
            {
                Device = deviceModel.ToDto(),
                Usage = usages.FirstOrDefault(usage => usage.DeviceId == deviceModel.DeviceId)?.Usage
            }).ToArray();

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res;
    }
}