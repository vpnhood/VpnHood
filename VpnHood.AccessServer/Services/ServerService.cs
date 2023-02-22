using System.Threading.Tasks;
using System;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Dtos;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Clients;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Services;

public class ServerService
{
    private readonly VhContext _vhContext;
    private readonly AppOptions _appOptions;
    private readonly AgentCacheClient _agentCacheClient;


    public ServerService(
        VhContext vhContext,
        IOptions<AppOptions> appOptions,
        AgentCacheClient agentCacheClient)
    {
        _vhContext = vhContext;
        _appOptions = appOptions.Value;
        _agentCacheClient = agentCacheClient;
    }

    public async Task<ServerData[]> List(Guid projectId,
        Guid? serverId = null,
        Guid? serverFarmId = null,
        int recordIndex = 0,
        int recordCount = int.MaxValue)
    {
        // no lock
        await using var trans = await _vhContext.WithNoLockTransaction();

        var query = _vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Include(server => server.AccessPointGroup)
            .Include(server => server.AccessPoints!)
            .ThenInclude(accessPoint => accessPoint.AccessPointGroup)
            .Where(server => serverId == null || server.ServerId == serverId)
            .Where(server => serverFarmId == null || server.AccessPointGroupId == serverFarmId);

        var serverModels = await query
            .AsNoTracking()
            .OrderBy(x => x.ServerId)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        // update all status
        var serverStatus = await _vhContext.ServerStatuses
            .AsNoTracking()
            .Where(serverStatus =>
                serverStatus.IsLast && serverStatus.ProjectId == projectId &&
                (serverId == null || serverStatus.ServerId == serverId) &&
                (serverFarmId == null || serverStatus.Server!.AccessPointGroupId == serverFarmId))
            .ToDictionaryAsync(x => x.ServerId);

        foreach (var serverModel in serverModels)
            if (serverStatus.TryGetValue(serverModel.ServerId, out var serverStatusEx))
                serverModel.ServerStatus = serverStatusEx;

        // create Dto
        var serverDatas = serverModels
            .Select(serverModel => new ServerData
            {
                AccessPoints = serverModel.AccessPoints!.Select(x => x.ToDto(x.AccessPointGroup?.AccessPointGroupName)).ToArray(),
                Server = serverModel.ToDto(
                    serverModel.AccessPointGroup?.AccessPointGroupName,
                    serverModel.ServerStatus?.ToDto(),
                    _appOptions.LostServerThreshold)
            })
            .ToArray();

        // update from cache
        var cachedServers = await _agentCacheClient.GetServers(projectId);
        foreach (var serverData in serverDatas)
        {
            var cachedServer = cachedServers.SingleOrDefault(x => x.ServerId == serverData.Server.ServerId);
            if (cachedServer == null) continue;
            serverData.Server.ServerStatus = cachedServer.ServerStatus;
            serverData.Server.ServerState = cachedServer.ServerState;
        }

        return serverDatas;
    }

}
