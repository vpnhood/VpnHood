using System;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using GrayMint.Authorization.RoleManagement.RoleAuthorizations;
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
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/access-tokens")]
public class AccessTokensController : ControllerBase
{
    private readonly UsageReportService _usageReportService;
    private readonly SubscriptionService _subscriptionService;
    private readonly VhContext _vhContext;

    public AccessTokensController(
        UsageReportService usageReportService, 
        SubscriptionService subscriptionService, 
        VhContext vhContext)
    {
        _usageReportService = usageReportService;
        _subscriptionService = subscriptionService;
        _vhContext = vhContext;
    }

    [HttpPost]
    [AuthorizePermission(Permissions.AccessTokenWrite)]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateAccessTokens");
        await _subscriptionService.AuthorizeCreateAccessToken(projectId);

        var serverFarm = await _vhContext.ServerFarms
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == createParams.ServerFarmId);

        // create support id
        var supportCode = await _vhContext.AccessTokens
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

        await _vhContext.AccessTokens.AddAsync(accessToken);
        await _vhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    [HttpPatch("{accessTokenId:guid}")]
    [AuthorizePermission(Permissions.AccessTokenWrite)]
    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        // validate accessTokenModel.ServerFarmId
        var serverFarm = (updateParams.ServerFarmId != null)
            ? await _vhContext.ServerFarms.SingleAsync(x =>
                x.ProjectId == projectId &&
                x.ServerFarmId == updateParams.ServerFarmId)
            : null;

        // update
        var accessToken = await _vhContext.AccessTokens
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

        if (_vhContext.ChangeTracker.HasChanges())
            accessToken.ModifiedTime = DateTime.UtcNow;
        await _vhContext.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    [HttpGet("{accessTokenId:guid}/access-key")]
    [AuthorizePermission(Permissions.AccessTokenReadAccessKey)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await _vhContext
            .AccessTokens
            .Include(x => x.ServerFarm)
            .Include(x => x.ServerFarm!.Certificate)
            .Where(x => x.AccessTokenId == accessTokenId)
            .SingleAsync();

        var certificate = accessToken.ServerFarm!.Certificate!;
        var x509Certificate = new X509Certificate2(certificate.RawData);

        // find all public accessPoints 
        var farmServers = await _vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Where(server => server.ServerFarmId == accessToken.ServerFarmId )
            .ToArrayAsync();

        var tokenAccessPoints = farmServers
            .SelectMany(server => server.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

       
        if (VhUtil.IsNullOrEmpty(tokenAccessPoints))
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
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null, DateTime? usageEndTime = null)
    {
        var items = await List(projectId, accessTokenId: accessTokenId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);
        return items.Items.Single();
    }

    [HttpGet]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<ListResult<AccessTokenData>> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        await _subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        // no lock
        await using var trans = await _vhContext.WithNoLockTransaction();

        if (!Guid.TryParse(search, out var searchGuid)) searchGuid = Guid.Empty;
        if (!int.TryParse(search, out var searchInt)) searchInt = -1;

        // find access tokens
        var baseQuery =
            from accessToken in _vhContext.AccessTokens
            join serverFarm in _vhContext.ServerFarms on accessToken.ServerFarmId equals serverFarm.ServerFarmId
            join access in _vhContext.Accesses on new { accessToken.AccessTokenId, DeviceId = (Guid?)null } equals new { access.AccessTokenId, access.DeviceId } into accessGrouping
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
            var usages = await _usageReportService.GetAccessTokensUsage(projectId, accessTokenIds, serverFarmId, usageBeginTime, usageEndTime);

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


    [HttpDelete("{accessTokenId:guid}")]
    [AuthorizePermission(Permissions.AccessTokenWrite)]
    public async Task Delete(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await _vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ProjectId == projectId && !x.IsDeleted && x.AccessTokenId == accessTokenId);

        accessToken.IsDeleted = true;
        await _vhContext.SaveChangesAsync();
    }
}