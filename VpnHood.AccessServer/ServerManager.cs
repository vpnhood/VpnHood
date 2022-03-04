using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Collections;

namespace VpnHood.AccessServer;

public class ServerManager
{
    private readonly IOptions<AppOptions> _appOptions;
    private readonly TimeoutDictionary<Guid, TimeoutItem<Guid>> _devices = new();
    public bool AllowRedirect { get; set; } = true;
    
    public ServerManager(IOptions<AppOptions> appOptions)
    {
        _appOptions = appOptions;
        _devices.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(VhContext vhContext, Models.Server currentServer, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
    {
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
            select new { server, accessPoint, serverStatus };

        var accessPoints = await query.ToArrayAsync();
        var best = accessPoints
            .GroupBy(x => x.accessPoint.ServerId)
            .Select(x => x.First())
            .OrderBy(x => x.serverStatus.SessionCount)
            .FirstOrDefault();

        if (best != null)
        {
            _devices.TryAdd(deviceId, new(best.accessPoint.ServerId), true);
            var ret = new IPEndPoint(IPAddress.Parse(best.accessPoint.IpAddress), best.accessPoint.TcpPort);
            return ret;
        }

        return null;
    }

}