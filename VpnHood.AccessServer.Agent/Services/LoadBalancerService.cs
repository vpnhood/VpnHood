using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
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
            sessionRequestEx.RegionId, 
            sessionRequestEx.AllowRedirect);
    }

    private async Task CheckRedirect(AccessTokenModel accessToken, ServerCache currentServer, DeviceModel device,
        AddressFamily addressFamily, string? regionId, bool allowRedirect)
    {
        // server redirect is disabled by admin
        if (!agentOptions.Value.AllowRedirect)
            return;

        // get acceptable servers for this request
        var servers = await GetServersForRequest(accessToken.ServerFarmId, regionId);

        // accept current server (no redirect) if it is in acceptable list
        if (!allowRedirect)
        {
            if (servers.All(x => x.ServerId != currentServer.ServerId))
                throw new SessionExceptionEx(SessionErrorCode.AccessError, "You do not have access to this server.");

            return;
        }

        // get access points suitable for requests
        var accessPoints = new List<AccessPointModel>();
        foreach (var server in servers)
        {
            accessPoints.AddRange(server.AccessPoints.Where(accessPoint =>
                accessPoint.IsPublic && accessPoint.IpAddress.AddressFamily == addressFamily));
        }

        // convert access points to IPEndPoints
        var tcpEndPoints = accessPoints
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
            .ToArray();

        // no server found
        if (tcpEndPoints.Length == 0)
            throw new SessionExceptionEx(SessionErrorCode.AccessError, "Could not find any free server.");

        // deprecated: 505 client and later
        // client should send no redirect flag. for older version we keep last state in memory to prevent re-redirect
        var cacheKey = $"LastDeviceServer/{currentServer.ServerFarmId}/{device.DeviceId}/{regionId}";
        if (memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) && lastDeviceServerId == currentServer.ServerId)
        {
            if (servers.Any(x => x.ServerId == currentServer.ServerFarmId))
                return;
        }

        // redirect if current server does not serve the best TcpEndPoint
        if (!currentServer.AccessPoints.Any(accessPoint =>
                new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort).Equals(tcpEndPoints)))
        {
            memoryCache.Set(cacheKey, servers.First().ServerId, TimeSpan.FromMinutes(5));
            throw new SessionExceptionEx(new SessionResponseEx
            {
                ErrorCode = SessionErrorCode.RedirectHost,
                RedirectHostEndPoint = tcpEndPoints.First(),
                RedirectHostEndPoints = tcpEndPoints.Take(100).ToArray(),
            });
        }
    }

    private async Task<ServerCache[]> GetServersForRequest(Guid serverFarmId, string? regionIdString)
    {
        // find acceptable regions
        List<int>? regionIds = null;
        if (!string.IsNullOrWhiteSpace(regionIdString))
        {
            regionIds = new List<int>();
            if (!int.TryParse(regionIdString, out var regionId))
                throw new SessionExceptionEx(SessionErrorCode.SessionError, "Could not find any server for the requested region.");

            regionIds.Add(regionId);
        }

        // get all servers of this farm
        var servers = await cacheService.GetServers();

        // filter acceptable servers and sort them by load
        var farmServers = servers
            .Where(server =>
                server.IsReady &&
                server.ServerFarmId == serverFarmId &&
                (regionIds == null || regionIds.Contains(server.RegionId ?? -10)))
            .OrderBy(CalcServerLoad)
            .ToArray();

        return farmServers;
    }

    private static float CalcServerLoad(ServerCache server)
    {
        return (float)server.ServerStatus!.SessionCount / Math.Max(1, server.LogicalCoreCount);
    }
}