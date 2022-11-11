using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class UsageReportService
{
    private readonly VhReportContext _vhReportContext;
    private readonly IMemoryCache _memoryCache;

    public UsageReportService(VhReportContext vhReportContext, IMemoryCache memoryCache)
    {
        _vhReportContext = vhReportContext;
        _memoryCache = memoryCache;
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
        var queryAccessTokenIds = accessTokenIds is { Length: <= 50 } ? accessTokenIds : null;
        if (queryAccessTokenIds != null)
        {
            cacheKey = AccessUtil.GenerateCacheKey(
                $"accessToken_usage_{projectId}_{accessPointGroupId}_{string.Join(',', queryAccessTokenIds)}",
                usageStartTime, usageEndTime, out _);

            if (_memoryCache.TryGetValue(cacheKey, out usages))
                return usages;
        }

        // run the hard query
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
}