using System;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/access-tokens")]
public class AccessTokensController : SuperController<AccessTokensController>
{
    private readonly UsageReportService _usageReportService;

    public AccessTokensController(ILogger<AccessTokensController> logger, VhContext vhContext,
        UsageReportService usageReportService, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _usageReportService = usageReportService;
    }

    [HttpPost]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // find default serveEndPoint 
        await VerifyUserPermission(projectId, Permissions.AccessTokenWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateAccessTokens_{CurrentUserId}");
        if (await IsFreePlan(projectId) && VhContext.AccessTokens.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessTokenCount)
            throw new QuotaException(nameof(VhContext.AccessTokens), QuotaConstants.AccessTokenCount);

        var accessPointGroup = await VhContext.AccessPointGroups
            .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

        // create support id
        var supportCode = await VhContext.AccessTokens.Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.SupportCode) ?? 1000;
        supportCode++;

        var accessToken = new AccessTokenModel
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

        return accessToken.ToDto(accessToken.AccessPointGroup?.AccessPointGroupName);
    }

    [HttpPatch("{accessTokenId:guid}")]
    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.AccessTokenWrite);

        // validate accessTokenModel.AccessPointGroupId
        var accessPointGroup = (updateParams.AccessPointGroupId != null)
            ? await VhContext.AccessPointGroups.SingleAsync(x =>
                x.ProjectId == projectId && 
                x.AccessPointGroupId == updateParams.AccessPointGroupId)
            : null;

        // update
        var accessTokenModel = await VhContext.AccessTokens
            .Include(x=>x.AccessPointGroup)
            .SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
        if (updateParams.AccessTokenName != null) accessTokenModel.AccessTokenName = updateParams.AccessTokenName;
        if (updateParams.EndTime != null) accessTokenModel.EndTime = updateParams.EndTime;
        if (updateParams.Lifetime != null) accessTokenModel.Lifetime = updateParams.Lifetime;
        if (updateParams.MaxDevice != null) accessTokenModel.MaxDevice = updateParams.MaxDevice;
        if (updateParams.MaxTraffic != null) accessTokenModel.MaxTraffic = updateParams.MaxTraffic;
        if (updateParams.Url != null) accessTokenModel.Url = updateParams.Url;
        if (updateParams.AccessPointGroupId != null)
        {
            accessTokenModel.AccessPointGroupId = updateParams.AccessPointGroupId;
            accessTokenModel.AccessPointGroup = accessPointGroup;
        }
        VhContext.AccessTokens.Update(accessTokenModel);
        await VhContext.SaveChangesAsync();

        return accessTokenModel.ToDto(accessTokenModel.AccessPointGroup?.AccessPointGroupName);
    }

    [HttpGet("{accessTokenId:guid}/access-key")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        // get accessTokenModel with default accessPoint
        await VerifyUserPermission(projectId, Permissions.AccessTokenReadAccessKey);

        var accessToken = await VhContext
            .AccessTokens
            .Include(x => x.AccessPointGroup)
            .Include(x => x.AccessPointGroup!.Certificate)
            .Include(x => x.AccessPointGroup!.AccessPoints)
            .Where(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId)
            .SingleAsync();

        if (Util.IsNullOrEmpty(accessToken.AccessPointGroup?.AccessPoints?.ToArray()))
            throw new InvalidOperationException($"Could not find any access point for the {nameof(AccessPointGroupModel)}!");

        //var accessTokenModel = result.at;
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
        DateTime? usageStartTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);


        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

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
                accessTokenData = new AccessTokenData(accessToken.ToDto(accessPointGroup.AccessPointGroupName))
                {
                    Access = access!=null ? access.ToDto() : null
                }
            };

        query = query
            .AsNoTracking()
            .Skip(recordIndex)
            .Take(recordCount);

        var results = await query.ToArrayAsync();
        // fill usage if requested
        if (usageStartTime != null)
        {
            var accessTokenIds = results.Select(x => x.accessTokenData.AccessToken.AccessTokenId).ToArray();
            var usages = await _usageReportService.GetAccessTokensUsage(projectId, accessTokenIds, accessPointGroupId, usageStartTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.accessTokenData.AccessToken.AccessTokenId, out var usage))
                    result.accessTokenData.Usage = usage;
        }

        return results.Select(x => x.accessTokenData).ToArray();
    }


    [HttpDelete("{accessTokenId:guid}")]
    public async Task Delete(Guid projectId, Guid accessTokenId)
    {
        await VerifyUserPermission(projectId, Permissions.AccessTokenWrite);

        var accessToken = await VhContext.AccessTokens
            .SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);

        VhContext.AccessTokens.Remove(accessToken);
        await VhContext.SaveChangesAsync();
    }
}