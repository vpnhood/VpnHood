using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Persistence;

public class VhRepo(VhContext vhContext)
{
    public bool HasChanges()
    {
        return vhContext.ChangeTracker.HasChanges();
    }

    public ValueTask<EntityEntry> AddAsync(object model)
    {
        return vhContext.AddAsync(model);
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
}