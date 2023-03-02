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
using VpnHood.Server.Configurations;
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
    public async Task<SessionResponseBase> AddSessionUsage(Guid serverId, uint sessionId, bool closeSession, UsageInfo usageInfo)
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

        var serverFarm = await _vhContext.ServerFarms
            .Include(serverFarm => serverFarm.Certificate)
            .SingleAsync(serverFarm => serverFarm.ServerFarmId == server.ServerFarmId);

        return serverFarm.Certificate!.RawData;
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

        if (server.LastConfigCode?.ToString() != serverStatus.ConfigCode)
        {
            _logger.LogInformation(AccessEventId.Server,
                "Updating a server's LastConfigCode. ServerId: {ServerId}, ConfigCode: {ConfigCode}",
                server.ServerId, serverStatus.ConfigCode);

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
        var saveServer = string.IsNullOrEmpty(serverInfo.LastError) || serverInfo.LastError != server.LastConfigError;
        _logger.Log(saveServer ? LogLevel.Information : LogLevel.Trace, AccessEventId.Server,
            "Configuring a Server. ServerId: {ServerId}, Version: {Version}",
            server.ServerId, serverInfo.Version);

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
        if (server.AutoConfigure)
            server.AccessPoints = await CreateServerAccessPoints(server.ServerId, server.ServerFarmId, serverInfo);

        // update db if lastError has been changed; prevent bombing the db
        if (saveServer)
        {
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
            serverUpdate.AccessPoints = server.AccessPoints;
            await _vhContext.SaveChangesAsync();
        }

        var ipEndPoints = server.AccessPoints
            .Where(accessPoint => accessPoint.IsListen)
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
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
            SessionOptions = new Server.Configurations.SessionOptions
            {
                Timeout = _agentOptions.SessionTemporaryTimeout,
                TcpBufferSize = ServerUtil.GetBestTcpBufferSize(server.TotalMemory),
                SyncInterval = _agentOptions.SessionSyncInterval,
            }
        };

        // old version does not support null values
        if (serverInfo.Version < new Version(2,7,355))
            ret.ApplyDefaults();

        return ret;
    }

    private async Task<List<AccessPointModel>> CreateServerAccessPoints(Guid serverId, Guid farmId, ServerInfo serverInfo)
    {
        // find all public accessPoints 
        var farmServers = await _vhContext.Servers
            .Where(server => server.ServerFarmId == farmId && !server.IsDeleted)
            .ToArrayAsync();

        // all old PublicInToken AccessPoints in the same farm
        var oldAccessPoints = farmServers
            .SelectMany(serverModel => serverModel.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToList();

        // create private addresses
        var accessPoints = serverInfo.PrivateIpAddresses
            .Distinct()
            .Where(ipAddress => !serverInfo.PublicIpAddresses.Any(x => x.Equals(ipAddress)))
            .Select(ipAddress => new AccessPointModel
            {
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                IpAddress = ipAddress,
                TcpPort = 443,
                UdpPort = 0
            })
            .ToList();

        // create public addresses and try to save last publicInToken state
        accessPoints.AddRange(serverInfo.PublicIpAddresses
            .Distinct()
            .Select(ipAddress => new AccessPointModel
            {
                AccessPointMode = oldAccessPoints.Any(x => x.IpAddress.Equals(ipAddress))
                    ? AccessPointMode.PublicInToken // prefer last value
                    : AccessPointMode.Public,
                IsListen = serverInfo.PrivateIpAddresses.Any(x => x.Equals(ipAddress)),
                IpAddress = ipAddress,
                TcpPort = 443,
                UdpPort = 0
            }));

        // has other server in the farm offer any PublicInToken
        var hasOtherServerOwnPublicToken = farmServers.Any(server =>
            server.ServerId != serverId &&
            server.AccessPoints.Any(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken));

        // make sure at least one PublicInToken is selected
        if (!hasOtherServerOwnPublicToken)
        {
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetwork);
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetworkV6);
        }

        return accessPoints.ToList();
    }

    private static void SelectAccessPointAsPublicInToken(ICollection<AccessPointModel> accessPoints, AddressFamily addressFamily)
    {
        if (accessPoints.Any(x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress.AddressFamily == addressFamily))
            return; // already set

        var firstPublic = accessPoints.FirstOrDefault(x =>
            x.AccessPointMode == AccessPointMode.Public &&
            x.IpAddress.AddressFamily == addressFamily);

        if (firstPublic == null)
            return; // not public found to select as PublicInToken

        accessPoints.Remove(firstPublic);
        accessPoints.Add(new AccessPointModel
        {
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = firstPublic.IsListen,
            IpAddress = firstPublic.IpAddress,
            TcpPort = firstPublic.TcpPort,
            UdpPort = firstPublic.UdpPort
        });
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
