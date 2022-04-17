using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Collections;

namespace VpnHood.AccessServer;


public class ServerManager
{
    private readonly IOptions<AppOptions> _appOptions;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeoutDictionary<Guid, TimeoutItem<Guid>> _devices = new();
    public bool AllowRedirect { get; set; } = true;

    public ServerManager(IOptions<AppOptions> appOptions, IMemoryCache memoryCache)
    {
        _appOptions = appOptions;
        _memoryCache = memoryCache;
        _devices.Timeout = TimeSpan.FromMinutes(5);
    }

    private string GetServerMemKey(Guid serverId) => $"servers/{serverId}";

    public void InvalidateServer(Guid serverId)
    {
        _memoryCache.Remove(GetServerMemKey(serverId));
    }

    public async Task<Models.Server> GetServer(VhContext vhContext, Guid serverId)
    {
        var key = GetServerMemKey(serverId);

        if (_memoryCache.TryGetValue(key, out Models.Server server))
            return server;

        server = await vhContext.Servers.SingleAsync(x => x.ServerId == serverId);
        _memoryCache.Set(key, server);
        return server;
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(VhContext vhContext, Models.Server currentServer, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
    {
        //todo use server cache
        //todo check server status

        // prevent re-redirect if device has already redirected to this server
        if (!AllowRedirect || _devices.TryGetValue(deviceId, out var deviceItem) && deviceItem.Value == currentServer.ServerId && currentServer.IsEnabled)
            return currentEndPoint;

        var minStatusTime = DateTime.UtcNow - _appOptions.Value.ServerUpdateStatusInterval * 2;

        // get all public access points of group of active servers
        var query =
            from accessPoint in vhContext.AccessPoints
            join server in vhContext.Servers on accessPoint.ServerId equals server.ServerId
            join serverStatus in vhContext.ServerStatuses on server.ServerId equals serverStatus.ServerId
            where accessPoint.AccessPointGroupId == accessPointGroupId &&
                  (accessPoint.AccessPointMode == AccessPointMode.PublicInToken || accessPoint.AccessPointMode == AccessPointMode.Public) &&
                  //!serverStatus.IsConfigure && // server may fail to initialize itself after configuring itself
                  serverStatus.IsLast && serverStatus.CreatedTime > minStatusTime &&
                  server.IsEnabled
            select new
            {
                accessPoint.ServerId,
                serverStatus.SessionCount,
                EndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort),
            };

        var accessPoints = await query.ToArrayAsync();
        var best = accessPoints
            .Where(x => x.EndPoint.AddressFamily == currentEndPoint.AddressFamily)
            .GroupBy(x => x.ServerId)
            .Select(x => x.First())
            .OrderBy(x => x.SessionCount)
            .FirstOrDefault();

        if (best != null)
        {
            _devices.TryAdd(deviceId, new TimeoutItem<Guid>(best.ServerId), true);
            var ret = best.EndPoint;
            return ret;
        }

        return null;
    }
}