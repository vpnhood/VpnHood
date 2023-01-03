using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class AgentService
{
    private readonly CacheService _cacheService;
    private readonly SessionService _sessionService;
    private readonly ILogger<SessionService> _logger;
    private readonly VhContext _vhContext;
    private readonly AgentOptions _agentOptions;

    public AgentService(
        ILogger<SessionService> logger,
        IOptions<AgentOptions> agentOptions,
        CacheService cacheService,
        SessionService sessionService,
        VhContext vhContext)
    {
        _cacheService = cacheService;
        _sessionService = sessionService;
        _logger = logger;
        _vhContext = vhContext;
        _agentOptions = agentOptions.Value;
    }

    public async Task<ServerModel> GetServer(Guid serverId)
    {
        var server = await _cacheService.GetServer(serverId) ?? throw new Exception("Could not find server.");
        return server;
    }

    public async Task<SessionResponseEx> CreateSession(Guid serverId, SessionRequestEx sessionRequestEx)
    {
        var server = await GetServer(serverId);
        return await _sessionService.CreateSession(server, sessionRequestEx);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> GetSession(Guid serverId, uint sessionId, string hostEndPoint, string? clientIp)
    {
        var server = await GetServer(serverId);
        return await _sessionService.GetSession(server, sessionId, hostEndPoint, clientIp);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<ResponseBase> AddSessionUsage(Guid serverId, uint sessionId, bool closeSession, UsageInfo usageInfo)
    {
        var server = await GetServer(serverId);
        return await _sessionService.AddUsage(server, sessionId, usageInfo, closeSession);
    }

    [HttpGet("certificates/{hostEndPoint}")]
    public async Task<byte[]> GetCertificate(Guid serverId, string hostEndPoint)
    {
        var server = await GetServer(serverId);
        _logger.LogInformation(AccessEventId.Server, "Get certificate. ServerId: {ServerId}, HostEndPoint: {HostEndPoint}", 
            server.ServerId, hostEndPoint);

        var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        var accessPoint = await
            _vhContext.AccessPoints
                .Include(x => x.AccessPointGroup)
                .Include(x => x.AccessPointGroup!.Certificate)
                .SingleAsync(x => x.ServerId == server.ServerId &&
                                  x.IsListen &&
                                  x.TcpPort == requestEndPoint.Port &&
                                  (x.IpAddress == anyIp.ToString() ||
                                   x.IpAddress == requestEndPoint.Address.ToString()));


        return accessPoint.AccessPointGroup!.Certificate!.RawData;
    }

    private async Task CheckServerVersion(ServerModel server)
    {
        if (!string.IsNullOrEmpty(server.Version) && Version.Parse(server.Version) >= ServerUtil.MinServerVersion)
            return;

        var errorMessage = $"Your server version is not supported. Please update your server. MinSupportedVersion: {ServerUtil.MinServerVersion}";
        if (server.LastConfigError != errorMessage)
        {
            // update cache
            server.LastConfigError = errorMessage;
            await _cacheService.UpdateServer(server);

            // update db
            var serverUpdate = await _vhContext.Servers.FindAsync(server.ServerId) ?? throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverUpdate.LastConfigError = server.LastConfigError;
            await _vhContext.SaveChangesAsync();

        }
        throw new NotSupportedException(errorMessage);
    }

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(Guid serverId, ServerStatus serverStatus)
    {
        var server = await GetServer(serverId);
        await CheckServerVersion(server);
        SetServerStatus(server, serverStatus, false);

        if (server.LastConfigCode.ToString() != serverStatus.ConfigCode)
        {
            _logger.LogInformation(AccessEventId.Server,"Updating a LastConfigCode. ServerId: {ServerId}, ConfigCode: {ConfigCode}", server.ServerId, serverStatus.ConfigCode);

            // update cache
            server.LastConfigError = null;
            server.LastConfigCode = serverStatus.ConfigCode != null ? Guid.Parse(serverStatus.ConfigCode) : null;
            await _cacheService.UpdateServer(server);

            // update db
            var serverUpdate = await _vhContext.Servers.FindAsync(server.ServerId) ?? throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverUpdate.LastConfigError = server.LastConfigError;
            serverUpdate.LastConfigCode = server.LastConfigCode;
            await _vhContext.SaveChangesAsync();
        }

        var ret = new ServerCommand(server.ConfigCode.ToString());
        return ret;
    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(Guid serverId, ServerInfo serverInfo)
    {
        var server = await GetServer(serverId);
        var project = server.Project ?? throw new Exception("Server project has not been loaded.");
        _logger.LogInformation(AccessEventId.Server, "Configuring a Server. ServerId: {ServerId}, Version: {Version}", server.ServerId, serverInfo.Version);

        // must after assigning version 
        server.Version = serverInfo.Version.ToString();
        await CheckServerVersion(server);

        // update cache
        server.Version = serverInfo.Version.ToString();
        server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        server.OsInfo = serverInfo.OsInfo;
        server.MachineName = serverInfo.MachineName;
        server.ConfigureTime = DateTime.UtcNow;
        server.TotalMemory = serverInfo.TotalMemory ?? 0;
        server.LogicalCoreCount = serverInfo.LogicalCoreCount;
        server.Version = serverInfo.Version.ToString();
        server.LastConfigError = serverInfo.LastError;
        SetServerStatus(server, serverInfo.Status, true);

        // Update AccessPoints
        if (server.AccessPointGroupId != null)
            await UpdateServerAccessPoints(_vhContext, server, serverInfo);

        // update db
        var serverUpdate = await _vhContext.Servers.FindAsync(server.ServerId) ?? throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
        serverUpdate.Version = server.Version;
        serverUpdate.EnvironmentVersion = server.EnvironmentVersion;
        serverUpdate.OsInfo = server.OsInfo;
        serverUpdate.MachineName = server.MachineName;
        serverUpdate.ConfigureTime = server.ConfigureTime;
        serverUpdate.LogicalCoreCount = server.LogicalCoreCount;
        serverUpdate.TotalMemory = server.TotalMemory;
        serverUpdate.Version = server.Version;
        serverUpdate.LastConfigError = server.LastConfigError;
        await _vhContext.SaveChangesAsync();

        // read server accessPoints
        server.AccessPoints = await _vhContext.AccessPoints
            .Where(x => x.ServerId == server.ServerId)
            .ToArrayAsync();

        var ipEndPoints = server.AccessPoints
            .Where(x => x.IsListen)
            .Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort))
            .ToArray();

        var trackClientIp = project.TrackClientIp;
        var trackClientRequest = project.TrackClientRequest;

        var ret = new ServerConfig
        {
            TcpEndPoints = ipEndPoints,
            ConfigCode = server.ConfigCode.ToString(),
            UpdateStatusInterval = _agentOptions.ServerUpdateStatusInterval,
            TrackingOptions = new TrackingOptions
            {
                TrackClientIp = trackClientIp,
                TrackLocalPort = trackClientRequest is TrackClientRequest.LocalPort or TrackClientRequest.LocalPortAndDstPort or TrackClientRequest.LocalPortAndDstPortAndDstIp,
                TrackDestinationPort = trackClientRequest is TrackClientRequest.LocalPortAndDstPort or TrackClientRequest.LocalPortAndDstPortAndDstIp,
                TrackDestinationIp = trackClientRequest is TrackClientRequest.LocalPortAndDstPortAndDstIp,
            },
            SessionOptions = new Server.SessionOptions
            {
                Timeout = _agentOptions.SessionTemporaryTimeout,
                TcpBufferSize = ServerUtil.GetBestTcpBufferSize(server.TotalMemory),
                SyncInterval = _agentOptions.SessionSyncInterval,
            }
        };

        return ret;
    }

    private static bool AccessPointEquals(AccessPointModel value1, AccessPointModel value2)
    {
        return
            value1.ServerId.Equals(value2.ServerId) &&
            value1.IpAddress.Equals(value2.IpAddress) &&
            value1.IsListen.Equals(value2.IsListen) &&
            value1.AccessPointGroupId.Equals(value2.AccessPointGroupId) &&
            value1.AccessPointMode.Equals(value2.AccessPointMode) &&
            value1.TcpPort.Equals(value2.TcpPort) &&
            value1.UdpPort.Equals(value2.UdpPort);
    }

    private static async Task UpdateServerAccessPoints(VhContext vhContext, ServerModel server, ServerInfo serverInfo)
    {
        if (server.AccessPointGroupId == null)
            throw new InvalidOperationException($"{nameof(server.AccessPointGroupId)} is not set!");

        // find current tokenAccessPoints in AccessPointGroup
        var tokenAccessPoints = await vhContext.AccessPoints
            .Where(x =>
                x.AccessPointGroupId == server.AccessPointGroupId &&
                x.AccessPointMode == AccessPointMode.PublicInToken)
            .AsNoTracking()
            .ToArrayAsync();

        // create private addresses
        var accessPoints =
            (from ipAddress in serverInfo.PrivateIpAddresses.Distinct()
             where !serverInfo.PublicIpAddresses.Any(x => x.Equals(ipAddress))
             select new AccessPointModel
             {
                 AccessPointId = Guid.NewGuid(),
                 ServerId = server.ServerId,
                 AccessPointGroupId = server.AccessPointGroupId.Value,
                 AccessPointMode = AccessPointMode.Private,
                 IsListen = true,
                 IpAddress = ipAddress.ToString(),
                 TcpPort = 443,
                 UdpPort = 0
             }).ToList();

        // create public addresses
        accessPoints.AddRange(serverInfo.PublicIpAddresses
            .Distinct()
            .Select(ipAddress => new AccessPointModel
            {
                AccessPointId = Guid.NewGuid(),
                ServerId = server.ServerId,
                AccessPointGroupId = server.AccessPointGroupId.Value,
                AccessPointMode = tokenAccessPoints.Any(x => IPAddress.Parse(x.IpAddress).Equals(ipAddress))
                    ? AccessPointMode.PublicInToken
                    : AccessPointMode.Public, // prefer last value
                IsListen = serverInfo.PrivateIpAddresses.Any(x => x.Equals(ipAddress)),
                IpAddress = ipAddress.ToString(),
                TcpPort = 443,
                UdpPort = 0
            }));

        // Select first publicIp as a tokenAccessPoint if there is no tokenAccessPoint in other server of same group
        var firstPublicAccessPoint = accessPoints.FirstOrDefault(x => x.AccessPointMode == AccessPointMode.Public);
        if (tokenAccessPoints.All(x => x.ServerId == server.ServerId) &&
            accessPoints.All(x => x.AccessPointMode != AccessPointMode.PublicInToken) &&
            firstPublicAccessPoint != null)
            firstPublicAccessPoint.AccessPointMode = AccessPointMode.PublicInToken;

        // start syncing
        // remove old access points
        var curAccessPoints = server.AccessPoints?.ToArray() ?? Array.Empty<AccessPointModel>();
        vhContext.AccessPoints.RemoveRange(curAccessPoints.Where(x => !accessPoints.Any(y => AccessPointEquals(x, y))));

        // add new access points
        var newAccessPoints = accessPoints.Where(x => !curAccessPoints.Any(y => AccessPointEquals(x, y))).ToArray();
        await vhContext.AccessPoints.AddRangeAsync(newAccessPoints);
    }


    private static void SetServerStatus(ServerModel server, ServerStatus serverStatus, bool isConfigure)
    {
        var serverStatusEx = new ServerStatusModel
        {
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            IsConfigure = isConfigure,
            IsLast = true,
            CreatedTime = DateTime.UtcNow,
            AvailableMemory = serverStatus.AvailableMemory,
            CpuUsage = (byte?)serverStatus.CpuUsage,
            TcpConnectionCount = serverStatus.TcpConnectionCount,
            UdpConnectionCount = serverStatus.UdpConnectionCount,
            SessionCount = serverStatus.SessionCount,
            ThreadCount = serverStatus.ThreadCount,
            TunnelReceiveSpeed = serverStatus.TunnelReceiveSpeed,
            TunnelSendSpeed = serverStatus.TunnelSendSpeed
        };
        server.ServerStatus = serverStatusEx;
    }
}
