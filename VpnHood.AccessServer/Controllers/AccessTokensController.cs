using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using VpnHood.Common;
using VpnHood.Common.TokenLegacy;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/access-tokens")]
public class AccessTokensController(
    UsageReportService usageReportService,
    SubscriptionService subscriptionService,
    VhContext vhContext)
    : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateAccessTokens");
        await subscriptionService.AuthorizeCreateAccessToken(projectId);

        var serverFarm = await vhContext.ServerFarms
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == createParams.ServerFarmId);

        // create support id
        var supportCode = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.SupportCode) ?? 1000;
        supportCode++;

        var accessToken = new AccessTokenModel
        {
            AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
            ProjectId = projectId,
            ServerFarmId = serverFarm.ServerFarmId,
            AccessTokenName = createParams.AccessTokenName,
            MaxTraffic = createParams.MaxTraffic,
            MaxDevice = createParams.MaxDevice,
            ExpirationTime = createParams.ExpirationTime,
            Lifetime = createParams.Lifetime,
            Url = createParams.Url,
            IsPublic = createParams.IsPublic,
            Secret = createParams.Secret ?? VhUtil.GenerateKey(),
            SupportCode = supportCode,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            IsEnabled = true
        };

        await vhContext.AccessTokens.AddAsync(accessToken);
        await vhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    [HttpPatch("{accessTokenId}")]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        // validate accessTokenModel.ServerFarmId
        var serverFarm = (updateParams.ServerFarmId != null)
            ? await vhContext.ServerFarms.SingleAsync(x =>
                x.ProjectId == projectId &&
                x.ServerFarmId == updateParams.ServerFarmId)
            : null;

        // update
        var accessToken = await vhContext.AccessTokens
            .Include(x => x.ServerFarm)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.AccessTokenId == accessTokenId);
        if (updateParams.AccessTokenName != null) accessToken.AccessTokenName = updateParams.AccessTokenName;
        if (updateParams.ExpirationTime != null) accessToken.ExpirationTime = updateParams.ExpirationTime;
        if (updateParams.Lifetime != null) accessToken.Lifetime = updateParams.Lifetime;
        if (updateParams.MaxDevice != null) accessToken.MaxDevice = updateParams.MaxDevice;
        if (updateParams.MaxTraffic != null) accessToken.MaxTraffic = updateParams.MaxTraffic;
        if (updateParams.Url != null) accessToken.Url = updateParams.Url;
        if (updateParams.IsEnabled != null) accessToken.IsEnabled = updateParams.IsEnabled;
        if (updateParams.ServerFarmId != null)
        {
            accessToken.ServerFarmId = updateParams.ServerFarmId;
            accessToken.ServerFarm = serverFarm;
        }

        if (vhContext.ChangeTracker.HasChanges())
            accessToken.ModifiedTime = DateTime.UtcNow;
        await vhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

   

    [HttpGet("{accessTokenId:guid}/access-key")]
    [AuthorizeProjectPermission(Permissions.AccessTokenReadAccessKey)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await vhContext.AccessTokens
            .Include(x => x.ServerFarm)
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.AccessTokenId == accessTokenId)
            .SingleAsync();

        if (string.IsNullOrEmpty(accessToken.ServerFarm!.HostTokenJson))
            throw new InvalidOperationException("The Farm has not been configured or it does not have at least a server with a PublicInToken access points.");

        // create token
        var token = new Token
        {
            ServerToken = VhUtil.JsonDeserialize<ServerToken>(accessToken.ServerFarm.HostTokenJson),
            Secret = accessToken.Secret,
            TokenId = accessToken.AccessTokenId.ToString(),
            Name = accessToken.AccessTokenName,
            SupportId = accessToken.SupportCode.ToString()
        };

#pragma warning disable CS0618 // Type or member is obsolete
        return TokenV3.FromToken(token).ToAccessKey();
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [HttpGet("{accessTokenId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null, DateTime? usageEndTime = null)
    {
        var items = await List(projectId, accessTokenId: accessTokenId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);
        return items.Items.Single();
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ListResult<AccessTokenData>> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        // no lock
        await using var trans = await vhContext.WithNoLockTransaction();

        if (!Guid.TryParse(search, out var searchGuid)) searchGuid = Guid.Empty;
        if (!int.TryParse(search, out var searchInt)) searchInt = -1;

        // find access tokens
        var baseQuery =
            from accessToken in vhContext.AccessTokens
            join serverFarm in vhContext.ServerFarms on accessToken.ServerFarmId equals serverFarm.ServerFarmId
            join access in vhContext.Accesses on new { accessToken.AccessTokenId, DeviceId = (Guid?)null } equals new { access.AccessTokenId, access.DeviceId } into accessGrouping
            from access in accessGrouping.DefaultIfEmpty()
            where
                (accessToken.ProjectId == projectId && !accessToken.IsDeleted) &&
                (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                (serverFarmId == null || accessToken.ServerFarmId == serverFarmId) &&
                (string.IsNullOrEmpty(search) ||
                 (accessToken.AccessTokenId == searchGuid && searchGuid != Guid.Empty) ||
                 (accessToken.SupportCode == searchInt && searchInt != -1) ||
                 (accessToken.ServerFarmId == searchGuid && searchGuid != Guid.Empty) ||
                 accessToken.AccessTokenName!.StartsWith(search))
            orderby accessToken.SupportCode descending
            select new
            {
                serverFarm, // force to fetch serverFarm;
                accessTokenData = new AccessTokenData(accessToken.ToDto(serverFarm.ServerFarmName))
                {
                    Access = access != null ? access.ToDto() : null
                }
            };

        var query = baseQuery
            .AsNoTracking()
            .Skip(recordIndex)
            .Take(recordCount);

        var results = await query.ToArrayAsync();

        // fill usage if requested
        if (usageBeginTime != null)
        {
            var accessTokenIds = results.Select(x => x.accessTokenData.AccessToken.AccessTokenId).ToArray();
            var usages = await usageReportService.GetAccessTokensUsage(projectId, accessTokenIds, serverFarmId, usageBeginTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.accessTokenData.AccessToken.AccessTokenId, out var usage))
                    result.accessTokenData.Usage = usage;
        }

        var listResult = new ListResult<AccessTokenData>
        {
            Items = results.Select(x => x.accessTokenData),
            TotalCount = results.Length < recordCount ? recordIndex + results.Length : await baseQuery.LongCountAsync()
        };

        return listResult;
    }

    [HttpDelete]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public async Task DeleteMany(Guid projectId, Guid[] accessTokenIds)
    {
        var accessTokens = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => accessTokenIds.Contains(x.AccessTokenId))
            .ToListAsync();

        foreach (var accessToken in accessTokens)
            accessToken.IsDeleted = true;

        await vhContext.SaveChangesAsync();
    }



    [HttpDelete("{accessTokenId}")]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public async Task Delete(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.AccessTokenId == accessTokenId);

        accessToken.IsDeleted = true;
        await vhContext.SaveChangesAsync();
    }
}