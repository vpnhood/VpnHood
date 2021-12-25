using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Collections;

namespace VpnHood.AccessServer
{
    public class ServerManager
    {
        public ServerManager()
        {
        }

        //public void UpdateServer(Models.Server server)
        //{
        //    if (_serverStatuses.TryGetValue(server.ServerId, out var serverStatus))
        //        serverStatus.Server = server;
        //}

        //public void RemoveServer(Models.Server server)
        //{
        //    _serverStatuses.TryRemove(server.ServerId, out var _);
        //}

        //public void UpdateServerStatus(ServerStatusEx serverStatus)
        //{
        //    _serverStatuses.AddOrUpdate(serverStatus.ServerId, serverStatus, (key, oldValue) => serverStatus);
        //}

        private readonly TimeoutDictionary<Guid, TimeoutItem<Guid>> _devices = new ();
        public async Task<IPEndPoint?> FindBestServerForDevice(VhContext vhContext, Models.Server currentTerver, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
        {
            if (_devices.TryGetValue(deviceId, out var deviceItem) && deviceItem.Value == currentTerver.ServerId && currentTerver.IsEnabled)
                return currentEndPoint;

            var minStatusTime = DateTime.UtcNow - AccessServerApp.Instance.ServerUpdateStatusInverval; //todo

            // get all public access points of group of active servers
            var query =
                from accessPoint in vhContext.AccessPoints
                join server in vhContext.Servers on accessPoint.ServerId equals server.ServerId
                join serverStatus in vhContext.ServerStatuses on server.ServerId equals serverStatus.ServerId
                where accessPoint.AccessPointGroupId == accessPointGroupId &&
                    (accessPoint.AccessPointMode == AccessPointMode.PublicInToken || accessPoint.AccessPointMode == AccessPointMode.Public) &&
                    serverStatus.IsLast && serverStatus.CreatedTime > minStatusTime &&
                    server.IsEnabled
                select new { server, accessPoint, serverStatus };

            var accessPoints = await query.ToArrayAsync();
            var best = accessPoints
                .GroupBy(x => x.accessPoint.ServerId)
                .Select(x => x.First())
                .OrderBy(x => x.serverStatus.SessionCount)
                .FirstOrDefault();

            var ret = best != null 
                ? new IPEndPoint(IPAddress.Parse(best.accessPoint.IpAddress), best.accessPoint.TcpPort) 
                : null;

            return ret;

        }

    }
}