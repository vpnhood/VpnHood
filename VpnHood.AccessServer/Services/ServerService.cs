using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Dtos;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.Common.Utils;
using System.Net;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services;

public class ServerService
{
    private readonly VhContext _vhContext;
    private readonly AppOptions _appOptions;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly SubscriptionService _subscriptionService;

    public ServerService(
        VhContext vhContext,
        IOptions<AppOptions> appOptions,
        AgentCacheClient agentCacheClient,
        SubscriptionService subscriptionService)
    {
        _vhContext = vhContext;
        _appOptions = appOptions.Value;
        _agentCacheClient = agentCacheClient;
        _subscriptionService = subscriptionService;
    }

    public async Task<Dtos.Server> Create(Guid projectId, ServerCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"CreateServer_{projectId}");
        await _subscriptionService.AuthorizeCreateServer(projectId);

        // validate
        var serverFarm = await _vhContext.ServerFarms
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == createParams.ServerFarmId);

        // Resolve Name Template
        var serverName = createParams.ServerName?.Trim();
        if (string.IsNullOrWhiteSpace(serverName)) serverName = Resource.NewServerTemplate;
        if (serverName.Contains("##"))
        {
            var names = await _vhContext.Servers
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .Select(x => x.ServerName)
                .ToArrayAsync();
            serverName = AccessUtil.FindUniqueName(serverName, names);
        }

        var serverModel = new ServerModel
        {
            ProjectId = projectId,
            ServerId = Guid.NewGuid(),
            CreatedTime = DateTime.UtcNow,
            ServerName = serverName,
            IsEnabled = true,
            Secret = Util.GenerateSessionKey(),
            AuthorizationCode = Guid.NewGuid(),
            ServerFarmId = serverFarm.ServerFarmId,
            AccessPoints = ValidateAccessPoints(createParams.AccessPoints ?? Array.Empty<AccessPoint>()),
            ConfigCode = Guid.NewGuid(),
            AutoConfigure = createParams.AccessPoints == null,
        };

        await _vhContext.Servers.AddAsync(serverModel);
        await _vhContext.SaveChangesAsync();

        var server = serverModel.ToDto(
            serverFarm.ServerFarmName,
            null, _appOptions.LostServerThreshold);
        return server;

    }

    public async Task<Dtos.Server> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        if (updateParams.AutoConfigure?.Value == true && updateParams.AccessPoints != null)
            throw new ArgumentException($"{nameof(updateParams.AutoConfigure)} can not be true when {nameof(updateParams.AccessPoints)} is set", nameof(updateParams));

        // validate
        var server = await _vhContext.Servers
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerId == serverId);

        if (updateParams.ServerFarmId != null)
        {
            // make sure new access group belong to this account
            var serverFarm = await _vhContext.ServerFarms
                .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == updateParams.ServerFarmId);

            // update server serverFarm and all AccessPoints serverFarm
            server.ServerFarm = serverFarm;
            server.ServerFarmId = serverFarm.ServerFarmId;
        }
        if (updateParams.GenerateNewSecret?.Value == true) server.Secret = Util.GenerateSessionKey();
        if (updateParams.ServerName != null) server.ServerName = updateParams.ServerName;
        if (updateParams.AutoConfigure != null) server.AutoConfigure = updateParams.AutoConfigure;
        if (updateParams.AccessPoints != null)
        {
            server.AutoConfigure = false;
            server.AccessPoints = ValidateAccessPoints(updateParams.AccessPoints);
        }

        // reconfig if required
        if (updateParams.AccessPoints != null || updateParams.AutoConfigure != null || updateParams.AccessPoints != null || updateParams.ServerFarmId != null)
            server.ConfigCode = Guid.NewGuid();

        await _vhContext.SaveChangesAsync();
        var serverCache = await _agentCacheClient.GetServer(server.ServerId);
        await _agentCacheClient.InvalidateServer(server.ServerId);

        var serverDto = server.ToDto(
            server.ServerFarm?.ServerFarmName,
            serverCache?.ServerStatus,
            _appOptions.LostServerThreshold);

        return serverDto;
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
            .Include(server => server.ServerFarm)
            .Where(server => serverId == null || server.ServerId == serverId)
            .Where(server => serverFarmId == null || server.ServerFarmId == serverFarmId);

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
                (serverFarmId == null || serverStatus.Server!.ServerFarmId == serverFarmId))
            .ToDictionaryAsync(x => x.ServerId);

        foreach (var serverModel in serverModels)
            if (serverStatus.TryGetValue(serverModel.ServerId, out var serverStatusEx))
                serverModel.ServerStatus = serverStatusEx;

        // create Dto
        var serverDatas = serverModels
            .Select(serverModel => new ServerData
            {
                Server = serverModel.ToDto(
                    serverModel.ServerFarm?.ServerFarmName,
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

    private static List<AccessPointModel> ValidateAccessPoints(AccessPoint[] accessPoints)
    {
        if (accessPoints.Length > QuotaConstants.AccessPointCount)
            throw new QuotaException(nameof(QuotaConstants.AccessPointCount), QuotaConstants.AccessPointCount);

        //find duplicate tcp
        var duplicate = accessPoints
            .GroupBy(x => $"{x.IpAddress}:{x.TcpPort}-{x.IsListen}")
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .FirstOrDefault();

        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");

        //find duplicate tcp on any ipv4
        var anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.Any)).Select(x => x.TcpPort);
        duplicate = accessPoints.FirstOrDefault(x => x.IsListen && !x.IpAddress.Equals(IPAddress.Any) && anyPorts.Contains(x.TcpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");

        //find duplicate tcp on any ipv6
        anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.IPv6Any)).Select(x => x.TcpPort);
        duplicate = accessPoints.FirstOrDefault(x => x.IsListen && !x.IpAddress.Equals(IPAddress.IPv6Any) && anyPorts.Contains(x.TcpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");


        //find duplicate udp
        duplicate = accessPoints
            .Where(x => x.UdpPort != 0)
            .GroupBy(x => $"{x.IpAddress}:{x.UdpPort}-{x.IsListen}")
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .FirstOrDefault();

        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        //find duplicate udp on any ipv4
        anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.Any)).Select(x => x.UdpPort);
        duplicate = accessPoints.FirstOrDefault(x => x.IsListen && !x.IpAddress.Equals(IPAddress.Any) && anyPorts.Contains(x.UdpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        //find duplicate udp on any ipv6
        anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.IPv6Any)).Select(x => x.UdpPort);
        duplicate = accessPoints.FirstOrDefault(x => x.IsListen && !x.IpAddress.Equals(IPAddress.IPv6Any) && anyPorts.Contains(x.UdpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on same IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        return accessPoints.Select(x => x.ToModel()).ToList();
    }
}
