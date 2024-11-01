using System.Net;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Manager.Common.Utils;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class ServerSelectorService(
    CacheService cacheService)
{
    public async Task CheckRedirect(ServerCache currentServer, ServerSelectOptions options)
    {
        // get acceptable servers for this request
        var servers = await GetServersForRequest(options);

        // accept current server (no redirect) if it is in acceptable list
        if (!options.AllowRedirect) {
            if (servers.All(x => x.ServerId != currentServer.ServerId))
                throw new SessionExceptionEx(SessionErrorCode.AccessError, "This server can not serve you at this moment.");
            return;
        }

        // get access points suitable for requests
        var accessPoints = new List<AccessPointModel>();
        foreach (var server in servers) {
            var serverAccessPoints = server.AccessPoints
                .Where(x => x.IsPublic)
                .Where(x => x.IpAddress.IsV4() || options.IncludeIpV6)
                .OrderByDescending(x => x.IpAddress.AddressFamily); // prefer IPv6

            accessPoints.AddRange(serverAccessPoints);
        }

        // convert access points to IPEndPoints
        var tcpEndPoints = accessPoints
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
            .ToArray();

        // find the best TcpEndPoint for this request
        var bestTcpEndPoint = tcpEndPoints.FirstOrDefault();
        if (bestTcpEndPoint == null)
            throw new SessionExceptionEx(SessionErrorCode.AccessError,
                "Could not find any available server in the given location.");

        // redirect if current server does not serve the best TcpEndPoint
        var bestServer = servers.First(x => x.AccessPoints.Any(accessPoint =>
            bestTcpEndPoint.Equals(new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))));
        if (currentServer.ServerId != bestServer.ServerId) {
            throw new SessionExceptionEx(new SessionResponseEx {
                ErrorCode = SessionErrorCode.RedirectHost,
                RedirectHostEndPoint = bestTcpEndPoint,
                RedirectHostEndPoints = tcpEndPoints.Take(100).ToArray()
            });
        }
    }


    private async Task<ServerCache[]> GetServersForRequest(ServerSelectOptions options)
    {
        // get all servers of this farm
        var servers = await cacheService.GetServers(serverFarmId: options.ServerFarmCache.ServerFarmId);
        if (servers.Length == 0)
            return [];

        // filter acceptable servers and sort them by load
        var farmServers = servers
            .Where(server =>
                IsServerReadyForRedirect(server) &&
                (server.AllowInAutoLocation || !options.RequestedLocation.IsAuto()) &&
                (options.AllowedLocations == null || options.AllowedLocations.Contains(server.LocationInfo.CountryCode, StringComparer.OrdinalIgnoreCase)) &&
                server.LocationInfo.IsMatch(options.RequestedLocation) &&
                IsMatchClientFilter(options.ProjectCache, server, options.ClientTags))
            .OrderBy(CalcServerLoad)
            .ToArray();

        // filter servers by client filter policy
        return farmServers;
    }

    public static bool IsMatchClientFilter(ProjectCache project, ServerCache server, IEnumerable<string> clientTags)
    {
        if (server.ClientFilterId == null)
            return true;

        var clientFilter = project.ClientFilters.FirstOrDefault(x => x.ClientFilterId == server.ClientFilterId);
        if (clientFilter == null)
            return false;

        return ClientFilterUtils.Verify(clientFilter.Filter, clientTags);
    }

    // let's treat configuring servers as ready in respect of change ip and change certificate
    private static bool IsServerReadyForRedirect(ServerCache serverCache)
    {
        return
            serverCache.ServerState is ServerState.Idle or ServerState.Active or ServerState.Configuring &&
            serverCache.IsEnabled;
    }

    private static float CalcServerLoad(ServerCache server)
    {
        var power = Math.Max(0, server.Power ?? server.LogicalCoreCount);
        return (float)server.ServerStatus!.SessionCount / Math.Max(1, power);
    }

    public async Task<bool> IsAllPublicInTokenServersReady(Guid serverFarmId)
    {
        var servers = await cacheService.GetServers(serverFarmId: serverFarmId);

        // find all servers with access in tokens
        // Disabled servers are not counted as not ready because users turn them off intentionally
        servers = servers
            .Where(server =>
                server.ServerFarmId == serverFarmId &&
                server.AccessPoints.Any(x => x.AccessPointMode == AccessPointMode.PublicInToken))
            .ToArray();

        // at-least one server must be ready
        return servers.Length > 0 && servers.All(x => x.ServerState is ServerState.Idle or ServerState.Active);
    }
}