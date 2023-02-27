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
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using VpnHood.Common;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/access-tokens")]
public class AccessTokensController : SuperController<AccessTokensController>
{
    private readonly UsageReportService _usageReportService;
    private readonly SubscriptionService _subscriptionService;

    public AccessTokensController(
        ILogger<AccessTokensController> logger, 
        VhContext vhContext,
        UsageReportService usageReportService, 
        MultilevelAuthService multilevelAuthService, 
        SubscriptionService subscriptionService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _usageReportService = usageReportService;
        _subscriptionService = subscriptionService;
    }

    [HttpPost]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // find default serveEndPoint 
        await VerifyUserPermission(projectId, Permissions.AccessTokenWrite);

        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateAccessTokens");
        await _subscriptionService.AuthorizeCreateAccessToken(projectId);

        var serverFarm = await VhContext.ServerFarms
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == createParams.ServerFarmId);

        // create support id
        var supportCode = await VhContext.AccessTokens
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
            Secret = createParams.Secret ?? Util.GenerateSessionKey(),
            SupportCode = supportCode,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            IsEnabled = true
        };

        await VhContext.AccessTokens.AddAsync(accessToken);
        await VhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    [HttpPatch("{accessTokenId:guid}")]
    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.AccessTokenWrite);

        // validate accessTokenModel.ServerFarmId
        var serverFarm = (updateParams.ServerFarmId != null)
            ? await VhContext.ServerFarms.SingleAsync(x =>
                x.ProjectId == projectId &&
                x.ServerFarmId == updateParams.ServerFarmId)
            : null;

        // update
        var accessToken = await VhContext.AccessTokens
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

        if (VhContext.ChangeTracker.HasChanges())
            accessToken.ModifiedTime = DateTime.UtcNow;
        await VhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    [HttpGet("{accessTokenId:guid}/access-key")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        // get accessTokenModel with default accessPoint
        await VerifyUserPermission(projectId, Permissions.AccessTokenReadAccessKey);

        var accessToken = await VhContext
            .AccessTokens
            .Include(x => x.ServerFarm)
            .Include(x => x.ServerFarm!.Certificate)
            .Where(x => x.AccessTokenId == accessTokenId)
            .SingleAsync();

        var certificate = accessToken.ServerFarm!.Certificate!;
        var x509Certificate = new X509Certificate2(certificate.RawData);

        // find all public accessPoints 
        var farmServers = await VhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Where(server => server.ServerFarmId == accessToken.ServerFarmId )
            .ToArrayAsync();

        var tokenAccessPoints = farmServers
            .SelectMany(server => server.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

       
        if (Util.IsNullOrEmpty(tokenAccessPoints))
            throw new InvalidOperationException("Could not find any public access point for the ServerFarm. Please configure a server for this AccessToken.");

        // create token
        var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(), certificate.CommonName)
        {
            Version = 1,
            TokenId = accessToken.AccessTokenId,
            Name = accessToken.AccessTokenName,
            SupportId = accessToken.SupportCode,
            HostEndPoints = tokenAccessPoints.Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort)).ToArray(),
            HostPort = 0, //valid hostname is not supported yet
            IsValidHostName = false,
            IsPublic = accessToken.IsPublic,
            Url = accessToken.Url
        };

        return token.ToAccessKey();
    }

    [HttpGet("{accessTokenId:guid}")]
    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null, DateTime? usageEndTime = null)
    {
        var items = await List(projectId, accessTokenId: accessTokenId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);
        return items.Single();
    }

    [HttpGet]
    public async Task<AccessTokenData[]> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);


        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        if (!Guid.TryParse(search, out var searchGuid)) searchGuid = Guid.Empty;
        if (!int.TryParse(search, out var searchInt)) searchInt = -1;

        // find access tokens
        var query =
            from accessToken in VhContext.AccessTokens
            join serverFarm in VhContext.ServerFarms on accessToken.ServerFarmId equals serverFarm.ServerFarmId
            join access in VhContext.Accesses on new { accessToken.AccessTokenId, DeviceId = (Guid?)null } equals new { access.AccessTokenId, access.DeviceId } into accessGrouping
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

        query = query
            .AsNoTracking()
            .Skip(recordIndex)
            .Take(recordCount);

        var results = await query.ToArrayAsync();
        // fill usage if requested
        if (usageBeginTime != null)
        {
            var accessTokenIds = results.Select(x => x.accessTokenData.AccessToken.AccessTokenId).ToArray();
            var usages = await _usageReportService.GetAccessTokensUsage(projectId, accessTokenIds, serverFarmId, usageBeginTime, usageEndTime);

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
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ProjectId == projectId && !x.IsDeleted && x.AccessTokenId == accessTokenId);

        accessToken.IsDeleted = true;
        await VhContext.SaveChangesAsync();
    }
}