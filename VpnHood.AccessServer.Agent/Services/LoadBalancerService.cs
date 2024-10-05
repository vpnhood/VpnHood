using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Manager.Common.Utils;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class LoadBalancerService(
    CacheService cacheService,
    IOptions<AgentOptions> agentOptions)
{
    public Task CheckRedirect(AccessTokenModel accessToken, ServerCache currentServer, IList<string> clientTags,
        SessionRequestEx sessionRequestEx)
    {
        return CheckRedirect(
            accessToken: accessToken,
            currentServer: currentServer,
            clientTags: clientTags,
            addressFamily: sessionRequestEx.HostEndPoint.AddressFamily,
            locationPath: sessionRequestEx.ServerLocation,
            allowRedirect: sessionRequestEx.AllowRedirect);
    }

    private async Task CheckRedirect(AccessTokenModel accessToken, ServerCache currentServer, IEnumerable<string> clientTags,
        AddressFamily addressFamily, string? locationPath, bool allowRedirect)
    {
        // server redirect is disabled by admin
        if (!agentOptions.Value.AllowRedirect)
            return;

        // get acceptable servers for this request
        var servers = await GetServersForRequest(accessToken.ServerFarmId, locationPath, clientTags);

        // accept current server (no redirect) if it is in acceptable list
        if (!allowRedirect) {
            if (servers.All(x => x.ServerId != currentServer.ServerId))
                throw new SessionExceptionEx(SessionErrorCode.AccessError,
                    "This server can not serve you at this moment.");

            return;
        }

        // get access points suitable for requests
        var accessPoints = new List<AccessPointModel>();
        foreach (var server in servers) {
            var serverAccessPoints = server.AccessPoints
                .Where(x => x.IsPublic)
                .OrderByDescending(x => x.IpAddress.AddressFamily); // prefer IPv6

            accessPoints.AddRange(serverAccessPoints);
        }

        // convert access points to IPEndPoints
        var tcpEndPoints = accessPoints
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
            .ToArray();

        // find the best TcpEndPoint for this request
        var bestTcpEndPoint = tcpEndPoints.FirstOrDefault(x => // if client is PublicIpV4 the single redirect must be ipv4
            addressFamily == AddressFamily.InterNetworkV6 ||
            x.AddressFamily == AddressFamily.InterNetwork);

        // no server found
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

    private async Task<ServerCache[]> GetServersForRequest(Guid serverFarmId, string? locationPath, IEnumerable<string> clientTags)
    {
        // get all servers of this farm
        var servers = await cacheService.GetServers(serverFarmId: serverFarmId);
        if (servers.Length == 0) return [];

        var project = await cacheService.GetProject(servers.First().ProjectId);
        var requestLocation = ServerLocationInfo.Parse(locationPath ?? "*");

        // filter acceptable servers and sort them by load
        var farmServers = servers
            .Where(server =>
                IsServerReadyForRedirect(server) &&
                server.ServerFarmId == serverFarmId &&
                (server.AllowInAutoLocation || requestLocation.CountryCode != "*") &&
                server.LocationInfo.IsMatch(requestLocation) &&
                IsMatchClientFilter(project, server, clientTags))
            .OrderBy(CalcServerLoad)
            .ToArray();

        // filter servers by client filter policy

        return farmServers;
    }

    private static bool IsMatchClientFilter(ProjectCache project, ServerCache server, IEnumerable<string> clientTags)
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