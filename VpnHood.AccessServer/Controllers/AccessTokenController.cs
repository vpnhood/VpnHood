﻿using System;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/access-tokens")]
public class AccessTokenController : SuperController<AccessTokenController>
{
    private readonly VhReportContext _vhReportContext;
    private readonly IMemoryCache _memoryCache;

    public AccessTokenController(ILogger<AccessTokenController> logger, VhContext vhContext,
        VhReportContext vhReportContext, IMemoryCache memoryCache, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _vhReportContext = vhReportContext;
        _memoryCache = memoryCache;
    }

    [HttpPost]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // find default serveEndPoint 
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessTokenWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateAccessTokens_{CurrentUserId}");
        if (VhContext.AccessTokens.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessTokenCount)
            throw new QuotaException(nameof(VhContext.AccessTokens), QuotaConstants.AccessTokenCount);

        var accessPointGroup = await VhContext.AccessPointGroups
            .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

        // create support id
        var supportCode = await VhContext.AccessTokens.Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.SupportCode) ?? 1000;
        supportCode++;

        var accessToken = new AccessToken()
        {
            AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
            ProjectId = projectId,
            AccessPointGroupId = accessPointGroup.AccessPointGroupId,
            AccessTokenName = createParams.AccessTokenName,
            MaxTraffic = createParams.MaxTraffic,
            MaxDevice = createParams.MaxDevice,
            EndTime = createParams.EndTime,
            Lifetime = createParams.Lifetime,
            Url = createParams.Url,
            IsPublic = createParams.IsPublic,
            Secret = createParams.Secret ?? Util.GenerateSessionKey(),
            SupportCode = supportCode,
            CreatedTime = DateTime.UtcNow
        };

        await VhContext.AccessTokens.AddAsync(accessToken);
        await VhContext.SaveChangesAsync();
        return accessToken;
    }

    [HttpPatch("{accessTokenId:guid}")]
    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessTokenWrite);

        // validate accessToken.AccessPointGroupId
        if (updateParams.AccessPointGroupId != null)
            await VhContext.AccessPointGroups.SingleAsync(x =>
                x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);

        // update
        var accessToken = await VhContext.AccessTokens.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
        if (updateParams.AccessPointGroupId != null) accessToken.AccessPointGroupId = updateParams.AccessPointGroupId;
        if (updateParams.AccessTokenName != null) accessToken.AccessTokenName = updateParams.AccessTokenName;
        if (updateParams.EndTime != null) accessToken.EndTime = updateParams.EndTime;
        if (updateParams.Lifetime != null) accessToken.Lifetime = updateParams.Lifetime;
        if (updateParams.MaxDevice != null) accessToken.MaxDevice = updateParams.MaxDevice;
        if (updateParams.MaxTraffic != null) accessToken.MaxTraffic = updateParams.MaxTraffic;
        if (updateParams.Url != null) accessToken.Url = updateParams.Url;
        VhContext.AccessTokens.Update(accessToken);

        await VhContext.SaveChangesAsync();
        return accessToken;
    }

    [HttpGet("{accessTokenId:guid}/access-key")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        // get accessToken with default accessPoint
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessTokenReadAccessKey);

        var accessToken = await VhContext
            .AccessTokens
            .Include(x => x.AccessPointGroup)
            .Include(x => x.AccessPointGroup!.Certificate)
            .Include(x => x.AccessPointGroup!.AccessPoints)
            .Where(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId)
            .SingleAsync();

        if (Util.IsNullOrEmpty(accessToken.AccessPointGroup?.AccessPoints?.ToArray()))
            throw new InvalidOperationException($"Could not find any access point for the {nameof(AccessPointGroup)}!");

        //var accessToken = result.at;
        var certificate = accessToken.AccessPointGroup.Certificate!;
        var x509Certificate = new X509Certificate2(certificate.RawData);
        var accessPoints = accessToken.AccessPointGroup.AccessPoints
            .Where(x => x.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

        // create token
        var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(), certificate.CommonName)
        {
            Version = 1,
            TokenId = accessToken.AccessTokenId,
            Name = accessToken.AccessTokenName,
            SupportId = accessToken.SupportCode,
            HostEndPoints = accessPoints.Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort)).ToArray(),
            HostPort = 0, //valid hostname is not supported yet
            IsValidHostName = false,
            IsPublic = accessToken.IsPublic,
            Url = accessToken.Url
        };

        return token.ToAccessKey();
    }

    [HttpGet("{accessTokenId:guid}")]
    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageStartTime = null, DateTime? usageEndTime = null)
    {
        var items = await List(projectId, accessTokenId: accessTokenId,
            usageStartTime: usageStartTime, usageEndTime: usageEndTime);
        return items.Single();
    }

    [HttpGet]
    public async Task<AccessTokenData[]> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? accessPointGroupId = null,
        DateTime? usageStartTime = null, DateTime? usageEndTime = null, int recordIndex = 0, int recordCount = 51)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        if (!Guid.TryParse(search, out var searchGuid)) searchGuid = Guid.Empty;
        if (!int.TryParse(search, out var searchInt)) searchInt = -1;

        // find access tokens
        var query =
            from accessToken in VhContext.AccessTokens
            join accessPointGroup in VhContext.AccessPointGroups on accessToken.AccessPointGroupId equals accessPointGroup.AccessPointGroupId
            join access in VhContext.Accesses on new { accessToken.AccessTokenId, DeviceId = (Guid?)null } equals new { access.AccessTokenId, access.DeviceId } into accessGrouping
            from access in accessGrouping.DefaultIfEmpty()
            where
                (accessToken.ProjectId == projectId) &&
                (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                (string.IsNullOrEmpty(search) ||
                 (accessToken.AccessTokenId == searchGuid && searchGuid != Guid.Empty) ||
                 (accessToken.SupportCode == searchInt && searchInt != -1) ||
                 (accessToken.AccessPointGroupId == searchGuid && searchGuid != Guid.Empty) ||
                 accessToken.AccessTokenName!.StartsWith(search))
            orderby accessToken.SupportCode descending
            select new
            {
                accessPointGroup, // force to fetch accessPointGroup;
                accessTokenData = new AccessTokenData
                {
                    AccessToken = accessToken,
                    Access = access!=null ? AccessConverter.FromModel(access) : null
                }
            };

        query = query
            .AsNoTracking()
            .Skip(recordIndex)
            .Take(recordCount);

        var accessTokens = await query.ToArrayAsync();

        // fill usage if requested
        if (usageStartTime != null)
        {
            var accessTokenIds = accessTokens.Select(x => x.accessTokenData.AccessToken.AccessTokenId);
            var usagesQuery =
                from accessUsage in _vhReportContext.AccessUsages
                where
                    (accessUsage.ProjectId == projectId) &&
                    (accessTokenIds.Contains(accessUsage.AccessTokenId)) &&
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
                            AccessTokenCount = 1,
                        }
                        : null
                };
            var usages = await usagesQuery.ToArrayAsync();

            foreach (var accessToken in accessTokens)
            {
                accessToken.accessTokenData.Usage = usages.SingleOrDefault(x =>
                    x.AccessTokenId == accessToken.accessTokenData.AccessToken.AccessTokenId)?.Usage;
            }
        }

        return accessTokens.Select(x => x.accessTokenData).ToArray();
    }

    [HttpGet("{accessTokenId:guid}/devices")]
    public async Task<DeviceData[]> Devices(Guid projectId, Guid accessTokenId,
        DateTime? usageStartTime = null, DateTime? usageEndTime = null, int recordIndex = 0, int recordCount = 51)
    {
        usageStartTime ??= DateTime.UtcNow.AddDays(-1);
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"accessToken_devices_{projectId}_{recordIndex}_{recordCount}", usageStartTime, usageEndTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out DeviceData[] cacheRes))
            return cacheRes;

        // vhReportContext
        var usages = await
            _vhReportContext.AccessUsages
            .Where(accessUsage =>
                accessUsage.ProjectId == projectId &&
                accessUsage.AccessTokenId == accessTokenId &&
                accessUsage.CreatedTime >= usageStartTime &&
                (usageEndTime == null || accessUsage.CreatedTime <= usageEndTime))
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
        var devices = await VhContext.Devices
            .Where(device => device.ProjectId == projectId && deviceIds.Any(x => x == device.DeviceId))
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        // merge
        var res = devices.Select(device =>
            new DeviceData
            {
                Device = device,
                Usage = usages.FirstOrDefault(usage => usage.DeviceId == device.DeviceId)?.Usage
            }).ToArray();

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res;
    }


    [HttpDelete("{accessTokenId:guid}")]
    public async Task Delete(Guid projectId, Guid accessTokenId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessTokenWrite);

        var accessToken = await VhContext.AccessTokens
            .SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);

        VhContext.AccessTokens.Remove(accessToken);
        await VhContext.SaveChangesAsync();
    }
}