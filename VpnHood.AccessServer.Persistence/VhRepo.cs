using GrayMint.Common.Generics;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Views;

namespace VpnHood.AccessServer.Persistence;

public class VhRepo(VhContext vhContext) : RepoBase(vhContext)
{

    public Task<ServerFarmModel> ServerFarmGet(Guid projectId, Guid serverFarmId,
        bool includeServers = false, bool includeCertificate = false)
    {
        var query = vhContext.ServerFarms
            .Where(farm => farm.ProjectId == projectId && !farm.IsDeleted)
            .Where(farm => farm.ServerFarmId == serverFarmId);

        if (includeServers)
            query = query
                .Include(farm => farm.Servers!.Where(server => !server.IsDeleted));

        if (includeCertificate)
            query = query
                .Include(farm => farm.Certificate);

        return query.SingleAsync();
    }

    public Task<ServerModel> ServerGet(Guid projectId, Guid serverId, bool includeFarm = false, bool includeFarmProfile = false)
    {
        var query = vhContext.Servers
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.ServerId == serverId);

        if (includeFarm) query.Include(server => server.ServerFarm);
        if (includeFarmProfile) query.Include(server => server.ServerFarm!.ServerProfile);

        return query.SingleAsync();
    }

    public Task<CertificateModel> CertificateGet(Guid projectId, Guid certificateId)
    {
        var query = vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(certificate => certificate.CertificateId == certificateId && !certificate.IsDeleted);

        return query.SingleAsync();
    }

    public Task<ServerProfileModel> ServerProfileGet(Guid projectId, Guid serverProfileId)
    {
        var query = vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(serverProfile => serverProfile.ServerProfileId == serverProfileId && !serverProfile.IsDeleted);

        return query.SingleAsync();
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
            join access in vhContext.Accesses on new { accessToken.AccessTokenId, DeviceId = (Guid?)null } equals new
            { access.AccessTokenId, access.DeviceId } into accessGrouping
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
            select new AccessTokenView
            {
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

        var ret = new ListResult<AccessTokenView>()
        {
            Items = results,
            TotalCount = results.Length < recordCount ? recordIndex + results.Length : await baseQuery.LongCountAsync()
        };

        return ret;
    }

    public async Task AccessTokenDelete(Guid projectId, Guid[] accessTokenIds)
    {
        var accessTokens = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => accessTokenIds.Contains(x.AccessTokenId))
            .ToListAsync();

        foreach (var accessToken in accessTokens)
            accessToken.IsDeleted = true;
    }

    public Task<int> ServerFarmCount(Guid projectId, Guid? certificateId = null)
    {
        return vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.CertificateId == certificateId || certificateId == null)
            .CountAsync();
    }

    public async Task<IEnumerable<CertificateView>> CertificateList(Guid projectId, string? search = null,
        Guid? certificateId = null, bool includeSummary = false, int recordIndex = 0, int recordCount = 300)
    {
        var query = vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => certificateId == null || x.CertificateId == certificateId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.CommonName.Contains(search) ||
                x.CertificateId.ToString() == search);

        var res = await query
            .OrderBy(x => x.CommonName)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new CertificateView
            {
                Certificate = new CertificateModel //exclude RawData
                {
                    CertificateId = x.CertificateId,
                    SubjectName = x.SubjectName,
                    DnsVerificationText = x.DnsVerificationText,
                    CommonName = x.CommonName,
                    CreatedTime = x.CreatedTime,
                    ExpirationTime = x.ExpirationTime,
                    IssueTime = x.IssueTime,
                    IsDeleted = x.IsDeleted,
                    IsVerified = x.IsVerified,
                    ProjectId = x.ProjectId,
                    Thumbprint = x.Thumbprint,
                    RawData = Array.Empty<byte>()
                },
                ServerFarms = includeSummary
                    ? x.ServerFarms!
                        .Where(y => !y.IsDeleted)
                        .Select(y => IdName.Create(y.ServerFarmId, y.ServerFarmName))
                    : null
            })
            .AsNoTracking()
            .ToArrayAsync();

        return res;
    }

    public async Task CertificateDelete(Guid projectId, Guid certificateId)
    {
        var certificate = await vhContext.Certificates
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.CertificateId == certificateId)
            .SingleAsync();

        certificate.IsDeleted = true;
        await vhContext.SaveChangesAsync();
    }

    public Task<ProjectModel> ProjectGet(Guid projectId)
    {
        return vhContext.Projects
            .Where(x => x.ProjectId == projectId)
            .SingleAsync();
    }
}

