using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Utils;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class ServerConfigureService(
    IOptions<AppOptions> appOptions,
    VhRepo vhRepo,
    AgentCacheClient agentCacheClient)
{
    public async Task ReconfigServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null)
    {
        var servers = await vhRepo.ServerList(projectId, serverFarmId: serverFarmId, serverProfileId: serverProfileId);

        foreach (var server in servers)
            server.ConfigCode = Guid.NewGuid();

        await vhRepo.SaveChangesAsync();
        await agentCacheClient.InvalidateServers(projectId: projectId, serverFarmId: serverFarmId, serverProfileId: serverProfileId);
    }

    public async Task SaveChangesAndInvalidateServerFarm(Guid projectId, Guid serverFarmId, bool reconfigureServers)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId: serverFarmId, includeCertificates: true, includeServers: true);
        FarmTokenBuilder.UpdateIfChanged(serverFarm);
        if (reconfigureServers)
        {
            foreach (var server in serverFarm.Servers!)
                server.ConfigCode = Guid.NewGuid();
        }

        await vhRepo.SaveChangesAsync();
        await agentCacheClient.InvalidateServerFarm(serverFarmId: serverFarm.ServerFarmId, includeSevers: true);
    }

    public async Task<ServerCache?> SaveChangesAndInvalidateServer(Guid projectId, Guid serverId, bool reconfigure)
    {
        if (reconfigure)
        {
            var server = await vhRepo.ServerGet(projectId, serverId);
            server.ConfigCode = Guid.NewGuid();
            await SaveChangesAndInvalidateServerFarm(projectId, server.ServerFarmId, false);
        }
        else
        {
            await vhRepo.SaveChangesAsync();
            await agentCacheClient.InvalidateServers(projectId: projectId, serverId: serverId);
        }

        return await agentCacheClient.GetServer(serverId);
    }

    public async Task WaitForFarmConfiguration(Guid projectId, Guid serverFarmId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var servers = await agentCacheClient.GetServers(projectId, serverFarmId: serverFarmId);
            if (servers.All(server => server.ServerState != ServerState.Configuring))
                break;

            await Task.Delay(appOptions.Value.ServerUpdateStatusInterval / 3, cancellationToken);
        }
    }
}