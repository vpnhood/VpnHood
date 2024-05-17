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

    public async Task InvalidateServerFarm(Guid projectId, Guid serverFarmId, bool reconfigure)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId: serverFarmId, includeCertificate: true, includeServers: true);
        FarmTokenBuilder.UpdateIfChanged(serverFarm);
        if (reconfigure)
        {
            foreach (var server in serverFarm.Servers!)
                server.ConfigCode = Guid.NewGuid();
        }

        await vhRepo.SaveChangesAsync();
        await agentCacheClient.InvalidateServerFarm(serverFarmId: serverFarm.ServerFarmId, includeSevers: true);
    }

    public async Task<ServerCache?> InvalidateServer(Guid projectId, Guid serverId, bool reconfigure)
    {
        if (reconfigure)
        {
            var server = await vhRepo.ServerGet(projectId, serverId);
            var serverFarm = await vhRepo.ServerFarmGet(
                server.ProjectId, serverFarmId: server.ServerFarmId,
                includeCertificate: true, includeServers: true);

            var isFarmUpdated =  FarmTokenBuilder.UpdateIfChanged(serverFarm);
            server.ConfigCode = Guid.NewGuid();
            await vhRepo.SaveChangesAsync();
            if (isFarmUpdated)
                await agentCacheClient.InvalidateServerFarm(serverFarm.ServerFarmId, includeSevers: false);
        }

        await agentCacheClient.InvalidateServers(projectId: projectId, serverId: serverId);
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