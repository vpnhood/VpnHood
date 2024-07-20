using System.IO.IsolatedStorage;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class LoadBalancerService(
    CacheService cacheService,
    IOptions<AgentOptions> agentOptions,
    IMemoryCache memoryCache)
{
    public Task CheckRedirect(AccessTokenModel accessToken, ServerCache currentServer, DeviceModel device,
        SessionRequestEx sessionRequestEx)
    {
        return CheckRedirect(
            accessToken, currentServer, device,
            sessionRequestEx.HostEndPoint.AddressFamily,
            sessionRequestEx.ServerLocation,
            sessionRequestEx.AllowRedirect);
    }

    private async Task CheckRedirect(AccessTokenModel accessToken, ServerCache currentServer, DeviceModel device,
        AddressFamily addressFamily, string? locationPath, bool allowRedirect)
    {
        // server redirect is disabled by admin
        if (!agentOptions.Value.AllowRedirect)
            return;

        // get acceptable servers for this request
        var servers = await GetServersForRequest(accessToken.ServerFarmId, locationPath);

        // accept current server (no redirect) if it is in acceptable list
        if (!allowRedirect) {
            if (servers.All(x => x.ServerId != currentServer.ServerId))
                throw new SessionExceptionEx(SessionErrorCode.AccessError, "This server can not serve you at this moment.");

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
        var bestTcpEndPoint = tcpEndPoints.FirstOrDefault(x => // if client is IpV4 the single redirect must be ipv4
            addressFamily == AddressFamily.InterNetworkV6 ||
            x.AddressFamily == AddressFamily.InterNetwork);

        // no server found
        if (bestTcpEndPoint == null)
            throw new SessionExceptionEx(SessionErrorCode.AccessError, "Could not find any available server in the given location.");

        // todo
        // deprecated: 505 client and later
        // client should send no redirect flag. for older version we keep last state in memory to prevent re-redirect
        var cacheKey = $"LastDeviceServer/{currentServer.ServerFarmId}/{device.DeviceId}/{locationPath}";
        if (memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) &&
            lastDeviceServerId == currentServer.ServerId) {
            if (servers.Any(x => x.ServerId == currentServer.ServerId))
                return;
        }

        // redirect if current server does not serve the best TcpEndPoint
        var bestServer = servers.First(x => x.AccessPoints.Any(accessPoint =>
            bestTcpEndPoint.Equals(new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))));
        if (currentServer.ServerId != bestServer.ServerId) {
            memoryCache.Set(cacheKey, bestServer.ServerId, TimeSpan.FromMinutes(5));
            throw new SessionExceptionEx(new SessionResponseEx {
                ErrorCode = SessionErrorCode.RedirectHost,
                RedirectHostEndPoint = bestTcpEndPoint,
                RedirectHostEndPoints = tcpEndPoints.Take(100).ToArray()
            });
        }
    }

    private async Task<ServerCache[]> GetServersForRequest(Guid serverFarmId, string? locationPath)
    {
        // get all servers of this farm
        var servers = await cacheService.GetServers();
        var requestLocation = ServerLocationInfo.Parse(locationPath ?? "*");

        // filter acceptable servers and sort them by load
        var farmServers = servers
            .Where(server =>
                IsServerReadyForRedirect(server) &&
                server.ServerFarmId == serverFarmId &&
                (server.AllowInAutoLocation || requestLocation.CountryCode != "*") &&
                server.LocationInfo.IsMatch(requestLocation))
            .OrderBy(CalcServerLoad)
            .ToArray();

        return farmServers;
    }

    // let's treat configuring servers as ready in respect of change ip and change certificate
    private static bool IsServerReadyForRedirect(ServerCache serverCache)
    {
        return serverCache.ServerState is ServerState.Idle or ServerState.Active or ServerState.Configuring;
    }

    private static float CalcServerLoad(ServerCache server)
    {
        return (float)server.ServerStatus!.SessionCount / Math.Max(1, server.LogicalCoreCount);
    }

    public async Task<bool> IsAllPublicInTokenServersReady(Guid serverFarmId)
    {
        var servers = await cacheService.GetServers(serverFarmId: serverFarmId);

        // find all servers with access in tokens
        servers = servers
            .Where(server =>
                server.ServerFarmId == serverFarmId &&
                server.IsEnabled &&
                server.AccessPoints.Any(x => x.AccessPointMode == AccessPointMode.PublicInToken))
            .ToArray();

        // at-least one server must be ready
        return servers.Length > 0 && servers.All(x => x.ServerState is ServerState.Idle or ServerState.Active );
    }
}