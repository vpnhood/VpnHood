using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Collections;

namespace VpnHood.AccessServer;


public class ServerManager
{
    private readonly IOptions<AppOptions> _appOptions;
    private readonly SystemCache _systemCache;
    private readonly TimeoutDictionary<Guid, TimeoutItem<Guid>> _devices = new();
    public bool AllowRedirect { get; set; } = true;

    public ServerManager(IOptions<AppOptions> appOptions, SystemCache systemCache)
    {
        _appOptions = appOptions;
        _systemCache = systemCache;
        _devices.Timeout = TimeSpan.FromMinutes(5);
    }

    public ServerState GetServerState(Models.Server server, ServerStatusEx? serverStatus)
    {
        if (server.ConfigureTime == null) return ServerState.NotInstalled;
        if (serverStatus == null || serverStatus.CreatedTime < DateTime.UtcNow - _appOptions.Value.LostServerThreshold) return ServerState.Lost;
        if (server.ConfigCode != server.LastConfigCode) return ServerState.Configuring;
        if (!server.IsEnabled) return ServerState.Disabled;
        if (serverStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }

    public bool IsServerReady(Models.Server server)
    {
        var serverState = GetServerState(server, server.ServerStatus);
        return serverState is ServerState.Idle or ServerState.Active;
    }


    public async Task<IPEndPoint?> FindBestServerForDevice(VhContext vhContext, Models.Server currentServer, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
    {
        // prevent re-redirect if device has already redirected to this server
        if (!AllowRedirect || _devices.TryGetValue(deviceId, out var deviceItem) &&
            deviceItem.Value == currentServer.ServerId)
        {
            var currentServerStatus = _systemCache.GetServerStatus(currentServer.ServerId, null);
            if (currentServerStatus != null && IsServerReady(currentServer))
                return currentEndPoint;
        }


        // get all servers of this farm
        var servers = await _systemCache.GetServers(vhContext, currentServer.ProjectId);
        servers = servers.Where(IsServerReady).ToArray();

        // find all accessPoints belong to this farm
        var accessPoints = new List<AccessPoint>();
        foreach (var server in servers)
            foreach (var accessPoint in server.AccessPoints!.Where(x =>
                         x.AccessPointGroupId == accessPointGroupId &&
                         x.AccessPointMode is AccessPointMode.PublicInToken or AccessPointMode.Public &&
                         IPAddress.Parse(x.IpAddress).AddressFamily == currentEndPoint.AddressFamily))
            {
                accessPoint.Server = server;
                accessPoints.Add(accessPoint);
            }

        // find the best free server
        var best = accessPoints
            .GroupBy(x => x.ServerId)
            .Select(x => x.First())
            .MinBy(x => x.Server!.ServerStatus!.SessionCount);

        if (best != null)
        {
            _devices.TryAdd(deviceId, new TimeoutItem<Guid>(best.ServerId), true);
            var ret = new IPEndPoint(IPAddress.Parse(best.IpAddress), best.TcpPort);
            return ret;
        }

        return null;
    }
}