using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Services;

public class UsageReportService
{
    private const int SmallCacheLength = 50; 
    private readonly VhReportContext _vhReportContext;
    private readonly IMemoryCache _memoryCache;
    private readonly AppOptions _appOptions;

    public UsageReportService(
        VhReportContext vhReportContext, 
        IMemoryCache memoryCache, 
        IOptions<AppOptions> appOptions)
    {
        _vhReportContext = vhReportContext;
        _memoryCache = memoryCache;
        _appOptions = appOptions.Value;
    }

    public async Task<Usage> GetUsageSummary(Guid projectId, DateTime usageStartTime, DateTime? usageEndTime = null, 
        Guid? accessPointGroupId = null, Guid? serverId = null)
    {
        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"project_usage_{projectId}_{accessPointGroupId}_{serverId}", 
            usageStartTime, usageEndTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out Usage cacheRes))
            return cacheRes;

        // select and order
        await using var transReport = await _vhReportContext.WithNoLockTransaction();
        var query =
            from accessUsage in _vhReportContext.AccessUsages
            where
                (accessUsage.ProjectId == projectId) &&
                (accessUsage.CreatedTime >= usageStartTime) &&
                (serverId == null || accessUsage.ServerId == serverId) &&
                (accessPointGroupId == null || accessUsage.ServerId == accessPointGroupId) &&
                (usageEndTime == null || accessUsage.CreatedTime <= usageEndTime)
            group new { accessUsage } by true into g //todo check 
            select new Usage
            {
                DeviceCount = g.Select(y => y.accessUsage.DeviceId).Distinct().Count(),
                SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic),
            };

        var res = await query.SingleOrDefaultAsync() ?? new Usage { ServerCount = 0, DeviceCount = 0 };

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res;
    }

    public async Task<ServerUsage[]> GetUsageHistory(Guid projectId, DateTime usageStartTime, DateTime? usageEndTime = null,
        Guid? accessPointGroupId = null, Guid? serverId = null)
    {
        usageEndTime ??= DateTime.UtcNow;

        // no lock
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"project_usage_{projectId}_{accessPointGroupId}_{serverId}",
            usageStartTime, usageEndTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out ServerUsage[] cacheRes))
            return cacheRes;

        // go back to the time that ensure all servers sent their status
        var serverUpdateStatusInterval = _appOptions.ServerUpdateStatusInterval * 2;
        usageEndTime = usageEndTime.Value.Subtract(serverUpdateStatusInterval);
        var step1 = serverUpdateStatusInterval.TotalMinutes;
        var step2 = (int)Math.Max(step1, (usageEndTime.Value - usageStartTime).TotalMinutes / 12 / step1);

        var baseTime = usageStartTime;

        // per server in status interval
        var serverStatuses = _vhReportContext.ServerStatuses
            .Where(x => x.ProjectId == projectId && x.CreatedTime >= usageStartTime && x.CreatedTime <= usageEndTime)
            .GroupBy(serverStatus => new
            {
                Minutes = (long)(EF.Functions.DateDiffMinute(baseTime, serverStatus.CreatedTime) / step1),
                serverStatus.ServerId
            })
            .Select(g => new
            {
                g.Key.Minutes,
                g.Key.ServerId,
                SessionCount = g.Max(x => x.SessionCount),
                TunnelTransferSpeed = g.Max(x => x.TunnelReceiveSpeed + x.TunnelSendSpeed),
            });

        // sum of max in status interval
        var serverStatuses2 = serverStatuses
            .GroupBy(x => x.Minutes)
            .Select(g => new
            {
                Minutes = g.Key,
                SessionCount = g.Sum(x => x.SessionCount),
                TunnelTransferSpeed = g.Sum(x => x.TunnelTransferSpeed),
                // ServerCount = g.Count() 
            });

        // scale down and find max
        var totalStatuses = serverStatuses2
            .GroupBy(x => (int)(x.Minutes / step2))
            .Select(g =>
                new ServerUsage
                {
                    Time = baseTime.AddMinutes(g.Key * step2 * step1),
                    SessionCount = g.Max(y => y.SessionCount),
                    TunnelTransferSpeed = g.Max(y => y.TunnelTransferSpeed),
                    // ServerCount = g.Max(y=>y.ServerCount) 
                })
            .OrderBy(x => x.Time);

        var res = await totalStatuses.ToListAsync();

        // add missed step
        var stepSize = step2 * step1;
        var stepCount = (int)((usageEndTime - usageStartTime).Value.TotalMinutes / stepSize) + 1;
        for (var i = 0; i < stepCount; i++)
        {
            var time = usageStartTime.AddMinutes(i * stepSize);
            if (res.Count <= i || res[i].Time != time)
                res.Insert(i, new ServerUsage { Time = time });
        }

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res.ToArray();
    }


    public async Task<Dictionary<Guid, Usage>> GetAccessTokenUsages(Guid projectId, Guid[]? accessTokenIds = null, Guid? accessPointGroupId = null,
        DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        var cacheKey = AccessUtil.GenerateCacheKey($"accessToken_usage_{projectId}_{accessPointGroupId}",
            usageStartTime, usageEndTime, out var cacheExpiration);

        // look from big cache
        if (_memoryCache.TryGetValue(cacheKey, out Dictionary<Guid, Usage> usages))
        {
            // filter result by given accessTokenIds
            if (accessTokenIds != null)
                usages = usages.Where(x => accessTokenIds.Contains(x.Key))
                    .ToDictionary(p => p.Key, p => p.Value);

            return usages;

        }

        // look for small cache
        var queryAccessTokenIds = accessTokenIds is { Length: <= SmallCacheLength } ? accessTokenIds : null;
        if (queryAccessTokenIds != null)
        {
            cacheKey = AccessUtil.GenerateCacheKey(
                $"accessToken_usage_{projectId}_{accessPointGroupId}_{string.Join(',', queryAccessTokenIds)}",
                usageStartTime, usageEndTime, out _);

            if (_memoryCache.TryGetValue(cacheKey, out usages))
                return usages;
        }

        // run the hard query
        await using var transReport = await _vhReportContext.WithNoLockTransaction();
        var usagesQuery =
            from accessUsage in _vhReportContext.AccessUsages
            where
                (accessUsage.ProjectId == projectId) &&
                (queryAccessTokenIds == null || queryAccessTokenIds.Contains(accessUsage.AccessTokenId)) &&
                (accessUsage.CreatedTime >= usageStartTime) &&
                (usageEndTime == null || accessUsage.CreatedTime <= usageEndTime)
            group new { accessUsage } by (Guid?)accessUsage.AccessTokenId
            into g
            select new
            {
                AccessTokenId = g.Key,
                Usage = g.Key != null
                    ? new Usage
                    {
                        SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic),
                        DeviceCount = g.Select(y => y.accessUsage.DeviceId).Distinct().Count(),
                        SessionCount = g.Select(y => y.accessUsage.ServerId).Distinct().Count(),
                        AccessTokenCount = 1,
                    }
                    : null
            };

        usages = await usagesQuery
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AccessTokenId!.Value, x => x.Usage);

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, usages, cacheExpiration.Value);

        // filter result by given accessTokenIds
        if (accessTokenIds != null)
            usages = usages.Where(x => accessTokenIds.Contains(x.Key))
                .ToDictionary(p => p.Key, p => p.Value);

        return usages;
    }

    public async Task<Dictionary<Guid, TrafficUsage>> GetDeviceUsages(Guid projectId, 
        Guid[]? deviceIds = null, Guid? accessTokenId = null, Guid? accessPointGroupId = null, 
        DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        var cacheKey = AccessUtil.GenerateCacheKey($"device_usage_{projectId}_{accessTokenId}_{accessPointGroupId}",
            usageStartTime, usageEndTime, out var cacheExpiration);

        // look from big cache
        if (_memoryCache.TryGetValue(cacheKey, out Dictionary<Guid, TrafficUsage> usages))
        {
            // filter result by given deviceIds
            if (deviceIds != null)
                usages = usages.Where(x => deviceIds.Contains(x.Key))
                    .ToDictionary(p => p.Key, p => p.Value);

            return usages;
        }

        // look for small cache
        await using var transReport = await _vhReportContext.WithNoLockTransaction();
        var queryDeviceIds = deviceIds is { Length: <= SmallCacheLength } ? deviceIds : null;
        if (queryDeviceIds != null)
        {
            cacheKey = AccessUtil.GenerateCacheKey(
                $"device_usage_{projectId}_{accessTokenId}_{accessPointGroupId}_{string.Join(',', queryDeviceIds)}",
                usageStartTime, usageEndTime, out _);

            if (_memoryCache.TryGetValue(cacheKey, out usages))
                return usages;
        }

        // run the hard query
        var usagesQuery =
            from accessUsage in _vhReportContext.AccessUsages
            where
                (accessUsage.ProjectId == projectId) &&
                (queryDeviceIds == null || queryDeviceIds.Contains(accessUsage.DeviceId)) &&
                (accessUsage.CreatedTime >= usageStartTime) &&
                (usageEndTime == null || accessUsage.CreatedTime <= usageEndTime)
            group new { accessUsage } by (Guid?)accessUsage.DeviceId
            into g
            select new
            {
                DeviceId = g.Key,
                Usage = g.Key != null
                    ? new TrafficUsage
                    {
                        SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic),
                        LastUsedTime = g.Max(y => y.accessUsage.CreatedTime),
                    }
                    : null
            };

        usages = await usagesQuery
            .AsNoTracking()
            .ToDictionaryAsync(x => x.DeviceId!.Value, x => x.Usage);

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, usages, cacheExpiration.Value);

        // filter result by given deviceIds
        if (deviceIds != null)
            usages = usages.Where(x => deviceIds.Contains(x.Key))
                .ToDictionary(p => p.Key, p => p.Value);

        return usages;
    }
}