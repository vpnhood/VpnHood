﻿using GrayMint.Common.Generics;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence.Views;

namespace VpnHood.AccessServer.Persistence;

public class VhRepo(VhContext vhContext)
{
    public bool HasChanges()
    {
        return vhContext.ChangeTracker.HasChanges();
    }

    public async ValueTask<T> AddAsync<T>(T model) where T : class
    {
        var entityEntry = await vhContext.AddAsync(model);
        return entityEntry.Entity;
    }

    public Task SaveChangesAsync()
    {
        return vhContext.SaveChangesAsync();
    }

    public Task<ServerFarmModel> GetServerFarm(Guid projectId, Guid serverFarmId,
        bool includeServers = false, bool includeCertificate = false)
    {
        var query = vhContext.ServerFarms
            .Where(farm => farm.ProjectId == projectId)
            .Where(farm => farm.ServerFarmId == serverFarmId && !farm.IsDeleted);

        if (includeServers)
            query = query
                .Include(farm => farm.Servers)
                .Where(farm => farm.Servers!.All(x => !x.IsDeleted));

        if (includeCertificate)
            query = query
                .Include(farm => farm.Certificate);

        return query.SingleAsync();
    }

    public Task<ServerModel> GetServer(Guid projectId, Guid serverId, bool includeFarm = false)
    {
        var query = vhContext.Servers
            .Where(server => server.ProjectId == projectId)
            .Where(server => server.ServerId == serverId && !server.IsDeleted);

        if (includeFarm)
            query.Include(server => server.ServerFarm);

        return query.SingleAsync();
    }

    public Task<CertificateModel> GetCertificate(Guid projectId, Guid certificateId)
    {
        var query = vhContext.Certificates
            .Where(certificate => certificate.ProjectId == projectId)
            .Where(certificate => certificate.CertificateId == certificateId && !certificate.IsDeleted);

        return query.SingleAsync();
    }

    public Task<ServerProfileModel> GetServerProfile(Guid projectId, Guid serverProfileId)
    {
        var query = vhContext.ServerProfiles
            .Where(serverProfile => serverProfile.ProjectId == projectId)
            .Where(serverProfile => serverProfile.ServerProfileId == serverProfileId && !serverProfile.IsDeleted);

        return query.SingleAsync();
    }

    public async Task<string[]> GetServerNames(Guid projectId)
    {
        var names = await vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Select(server => server.ServerName)
            .ToArrayAsync();

        return names;

    }

    public async Task<int> GetMaxAccessTokenSupportCode(Guid projectId)
    {
        var res = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.SupportCode);

        return res ?? 1000;
    }

    public Task<AccessTokenModel> GetAccessToken(Guid projectId, Guid accessTokenId, bool includeServerFarm = false)
    {
        var query = vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId)
            .Where(x => x.AccessTokenId == accessTokenId);

        if (includeServerFarm)
            query = query.Include(x => x.ServerFarm);

        return query.SingleAsync();
    }

    public async Task<ListResult<AccessTokenView>> ListAccessTokenViews(Guid projectId, string? search = null,
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

    public async Task DeleteAccessToken(Guid projectId, Guid[] accessTokenIds)
    {
        var accessTokens = await vhContext.AccessTokens
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => accessTokenIds.Contains(x.AccessTokenId))
            .ToListAsync();

        foreach (var accessToken in accessTokens)
            accessToken.IsDeleted = true;
    }
}

