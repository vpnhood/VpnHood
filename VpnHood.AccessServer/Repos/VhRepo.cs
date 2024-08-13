using GrayMint.Common.Generics;
using Microsoft.EntityFrameworkCore;
using System.Net;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;
using VpnHood.AccessServer.Repos.Views;

namespace VpnHood.AccessServer.Repos;

public class VhRepo(VhContext vhContext)
    : RepoBase(vhContext)
{
    public Task<ServerModel> ServerGet(Guid projectId, Guid serverId, bool includeFarm = false,
        bool includeFarmProfile = false)
    {
        var query = vhContext.Servers
            .Include(x => x.Location)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.ServerId == serverId);

        if (includeFarm) query = query.Include(server => server.ServerFarm);
        if (includeFarmProfile) query = query.Include(server => server.ServerFarm!.ServerProfile);

        return query.SingleAsync();
    }

    public Task<ServerModel[]> ServerList(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null,
        bool includeServerFarm = false, bool tracking = true)
    {
        var query = vhContext.Servers
            .Include(x => x.Location)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.ServerFarmId == serverFarmId || serverFarmId == null)
            .Where(x => x.ServerFarm!.ServerProfileId == serverProfileId || serverProfileId == null);

        if (includeServerFarm)
            query = query.Include(x => x.ServerFarm);

        if (!tracking)
            query = query.AsNoTracking();

        return query.ToArrayAsync();
    }

    public async Task<ServerModel[]> ServerSearch(Guid projectId,
        string? search,
        Guid? serverId = null,
        Guid? serverFarmId = null,
        IPAddress? ipAddress = null,
        int recordIndex = 0,
        int recordCount = int.MaxValue)
    {
        await using var trans = await vhContext.WithNoLockTransaction();
        var servers = await vhContext.Servers
            .Include(server => server.ServerFarm)
            .Include(server => server.Location)
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Where(server => serverId == null || server.ServerId == serverId)
            .Where(server => serverFarmId == null || server.ServerFarmId == serverFarmId)
            .Where(server => ipAddress == null || server.AccessPoints.Any(y => y.IpAddress.Equals(ipAddress)))
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerName.Contains(search) ||
                x.ServerId.ToString() == search ||
                x.ServerFarmId.ToString() == search)
            .OrderBy(x => x.ServerName)
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        return servers;
    }


    public Task<ServerProfileModel> ServerProfileGet(Guid projectId, Guid serverProfileId)
    {
        var query = vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(serverProfile => serverProfile.ServerProfileId == serverProfileId && !serverProfile.IsDeleted);

        return query.SingleAsync();
    }

    public Task<ServerProfileModel> ServerProfileGetDefault(Guid projectId)
    {
        return vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.IsDefault)
            .SingleAsync();
    }


    public async Task<string[]> ServerGetNames(Guid projectId)
    {
        var names = await vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Select(server => server.ServerName)
            .ToArrayAsync();

        return names;
    }

    public async Task<int> AccessTokenGetMaxSupportCode(Guid projectId)
    {
        var res = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId) // include deleted ones
            .MaxAsync(x => (int?)x.SupportCode);

        return res ?? 1000;
    }

    public Task<AccessTokenModel> AccessTokenGet(Guid projectId, Guid accessTokenId, bool includeFarm = false)
    {
        var query = vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.AccessTokenId == accessTokenId);

        if (includeFarm)
            query = query.Include(x => x.ServerFarm);

        return query.SingleAsync();
    }

    public async Task<ListResult<AccessTokenView>> AccessTokenList(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
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
                (usageBeginTime == null || accessToken.LastUsedTime >= usageBeginTime) &&
                (usageEndTime == null || accessToken.LastUsedTime >= usageEndTime) &&
                (string.IsNullOrEmpty(search) ||
                 (accessToken.AccessTokenId == searchGuid && searchGuid != Guid.Empty) ||
                 (accessToken.SupportCode == searchInt && searchInt != -1) ||
                 (accessToken.ServerFarmId == searchGuid && searchGuid != Guid.Empty) ||
                 accessToken.AccessTokenName!.StartsWith(search))
            orderby accessToken.SupportCode descending
            select new AccessTokenView {
                ServerFarmName = serverFarm.ServerFarmName,
                AccessToken = accessToken,
                Access = access
            };

        var query = baseQuery
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking();

        var results = await query
            .ToArrayAsync();

        var ret = new ListResult<AccessTokenView> {
            Items = results,
            TotalCount = results.Length < recordCount ? recordIndex + results.Length : await baseQuery.LongCountAsync()
        };

        return ret;
    }

    public async Task<Guid[]> AccessTokenDelete(Guid projectId, Guid[] accessTokenIds)
    {
        var accessTokens = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => accessTokenIds.Contains(x.AccessTokenId))
            .ToListAsync();

        foreach (var accessToken in accessTokens)
            accessToken.IsDeleted = true;

        return accessTokens.Select(x => x.AccessTokenId).ToArray();
    }

    public Task<CertificateModel> CertificateGet(Guid projectId, Guid certificateId,
        bool includeProjectAndLetsEncryptAccount = false)
    {
        var query = vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(certificate => certificate.CertificateId == certificateId && !certificate.IsDeleted);

        if (includeProjectAndLetsEncryptAccount)
            query = query
                .Include(certificate => certificate.Project)
                .ThenInclude(projectModel => projectModel!.LetsEncryptAccount);

        return query.SingleAsync();
    }

    public async Task CertificateDelete(Guid projectId, Guid certificateId)
    {
        var certificate = await vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.CertificateId == certificateId)
            .SingleAsync();

        certificate.IsDeleted = true;
    }

    public Task<ProjectModel> ProjectGet(Guid projectId, bool includeLetsEncryptAccount = false)
    {
        var query = vhContext.Projects
            .Where(x => x.ProjectId == projectId);

        if (includeLetsEncryptAccount)
            query = query.Include(x => x.LetsEncryptAccount);

        return query.SingleAsync();
    }

    public Task<string[]> ServerFarmNames(Guid projectId)
    {
        return vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Select(x => x.ServerFarmName).ToArrayAsync();
    }

    public Task<AccessPointView[]> AccessPointListByFarms(IEnumerable<Guid> farmIds)
    {
        var query = vhContext.Servers
            .Where(x => !x.IsDeleted)
            .Where(x => farmIds.Contains(x.ServerFarmId))
            .Select(x => new AccessPointView {
                ServerFarmId = x.ServerFarmId,
                ServerId = x.ServerId,
                ServerName = x.ServerName,
                AccessPoints = x.AccessPoints.ToArray()
            });

        return query
            .AsNoTracking()
            .ToArrayAsync();
    }

    public async Task<ServerFarmModel> ServerFarmGet(Guid projectId, Guid serverFarmId,
        bool includeServers = false, bool includeAccessTokens = false, bool includeCertificates = false,
        bool includeProject = false,
        bool includeLetsEncryptAccount = false)
    {
        var query = vhContext.ServerFarms
            .Where(farm => farm.ProjectId == projectId && !farm.IsDeleted)
            .Where(farm => farm.ServerFarmId == serverFarmId);

        if (includeProject) {
            query = query.Include(x => x.Project);
            if (includeLetsEncryptAccount)
                query = query.Include(x => x.Project!.LetsEncryptAccount);
        }

        if (includeCertificates)
            query = query.Include(x => x.Certificates!.Where(y => !y.IsDeleted));

        if (includeServers)
            query = query.Include(farm => farm.Servers!.Where(server => !server.IsDeleted))
                .ThenInclude(x => x.Location);

        if (includeAccessTokens)
            query = query.Include(farm => farm.AccessTokens!.Where(accessToken => !accessToken.IsDeleted));

        var serverFarm = await query.SingleAsync();
        return serverFarm;
    }

    public Task<CertificateModel> ServerFarmGetInTokenCertificate(Guid projectId, Guid serverFarmId)
    {
        return vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.ServerFarmId == serverFarmId)
            .Where(x => !x.ServerFarm!.IsDeleted)
            .Where(x => x.IsInToken && !x.IsDeleted)
            .SingleAsync();
    }

    public async Task<ServerFarmView[]> ServerFarmListView(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        bool includeSummary = false, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerFarmName.Contains(search) ||
                x.ServerFarmId.ToString() == search)
            .Select(x => new ServerFarmView {
                ServerFarm = x,
                ServerProfileName = x.ServerProfile!.ServerProfileName,
                ServerCount = includeSummary ? x.Servers!.Count(y => !y.IsDeleted) : null,
                AccessTokens = includeSummary
                    ? x.AccessTokens!
                        .Where(y => !y.IsDeleted)
                        .Select(y => new ServerFarmView.AccessTokenView {
                            FirstUsedTime = y.FirstUsedTime,
                            LastUsedTime = y.LastUsedTime
                        }).ToArray()
                    : null
            });

        query = query.OrderByDescending(x => x.ServerFarm.ServerFarmName)
            .Skip(recordIndex)
            .Take(recordCount)
            .AsSplitQuery()
            .AsNoTracking();

        var results = await query.ToArrayAsync();
        return results;
    }

    public Task<CertificateModel[]> CertificateExpiringList(TimeSpan expireBy, int maxErrorCount,
        TimeSpan retryInterval)
    {
        var expirationTime = DateTime.UtcNow + expireBy;
        var errorTime = DateTime.UtcNow - retryInterval;

        var certificates = vhContext.Certificates
            .Where(x => !x.IsDeleted && x.AutoValidate)
            .Where(x => x.ValidateErrorCount < maxErrorCount)
            .Where(x => x.ValidateErrorTime == null || x.ValidateErrorTime < errorTime)
            .Where(x => x.ExpirationTime < expirationTime || !x.IsValidated)
            .ToArrayAsync();

        return certificates;
    }

    public Task<ProviderModel[]> ProviderList(Guid projectId, ProviderType providerType)
    {
        return vhContext.Providers
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.ProviderType == providerType)
            .ToArrayAsync();
    }

    public Task<ProviderModel> ProviderGet(Guid projectId, ProviderType providerType, string providerName)
    {
        return vhContext.Providers
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.ProviderType == providerType)
            .Where(x => x.ProviderName == providerName)
            .SingleAsync();
    }

    public Task<HostOrderModel[]> HostOrdersList(Guid projectId, HostOrderStatus? status = null, int recordIndex = 0,
        int recordCount = int.MaxValue)
    {
        return vhContext.HostOrders
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.Status == status || status == null)
            .OrderByDescending(x => x.CreatedTime)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();
    }

    public Task<HostOrderModel> HostOrderGet(Guid projectId, Guid hostOrderOd)
    {
        return vhContext.HostOrders
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.HostOrderId == hostOrderOd)
            .SingleAsync();
    }

    public Task<HostIpModel[]> HostIpList(Guid projectId)
    {
        return vhContext.HostIps
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .ToArrayAsync();
    }

    public Task<HostIpModel> HostIpGet(Guid projectId, IPAddress ipAddress)
    {
        return vhContext.HostIps
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.IpAddress == ipAddress)
            .SingleAsync();
    }
}