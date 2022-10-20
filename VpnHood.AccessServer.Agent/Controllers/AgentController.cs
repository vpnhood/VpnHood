using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using SessionOptions = VpnHood.Server.SessionOptions;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/agent")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme)]
public class AgentController : ControllerBase
{
    private readonly SessionRepo _sessionRepo;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<AgentController> _logger;
    private readonly VhContext _vhContext;
    private readonly CacheRepo _cacheRepo;

    public AgentController(ILogger<AgentController> logger, VhContext vhContext,
        SessionRepo sessionRepo,
        CacheRepo cacheRepo,
        IOptions<AgentOptions> agentOptions)
    {
        _cacheRepo = cacheRepo;
        _agentOptions = agentOptions.Value;
        _sessionRepo = sessionRepo;
        _logger = logger;
        _vhContext = vhContext;
    }

    private async Task<Models.Server> GetCallerServer()
    {
        try
        {
            // find serverId from identity claims
            var subject = User.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;
            var serverId = Guid.Parse(subject);
            var server = await _cacheRepo.GetServer(serverId) ?? throw new Exception("Could not find server.");
            return server;
        }
        catch (KeyNotFoundException)
        {
            throw new UnauthorizedAccessException();
        }

    }

    [HttpPost("sessions")]
    public async Task<SessionResponseEx> CreateSession(SessionRequestEx sessionRequestEx)
    {
        var server = await GetCallerServer();
        return await _sessionRepo.CreateSession(sessionRequestEx, server);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> GetSession(uint sessionId, string hostEndPoint, string? clientIp)
    {
        var server = await GetCallerServer();
        return await _sessionRepo.GetSession(sessionId, hostEndPoint, clientIp, server);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<ResponseBase> AddSessionUsage(uint sessionId, bool closeSession, UsageInfo usageInfo)
    {
        var server = await GetCallerServer();
        return await _sessionRepo.AddUsage(sessionId, usageInfo, closeSession, server);
    }

    [HttpGet("certificates/{hostEndPoint}")]
    public async Task<byte[]> GetCertificate(string hostEndPoint)
    {
        var server = await GetCallerServer();
        _logger.LogInformation("Get certificate. ServerId: {ServerId}, HostEndPoint: {HostEndPoint}", server.ServerId,
            hostEndPoint);

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

    private async Task CheckServerVersion(Models.Server server)
    {
        if (!string.IsNullOrEmpty(server.Version) && Version.Parse(server.Version) >= ServerUtil.MinServerVersion)
            return;

        var errorMessage = $"Your server version is not supported. Please update your server. MinSupportedVersion: {ServerUtil.MinServerVersion}";
        if (server.LastConfigError != errorMessage)
        {
            // update db
            _vhContext.Servers.Attach(server);
            server.LastConfigError = errorMessage;
            await _vhContext.SaveChangesAsync();

            // update cache
            _cacheRepo.UpdateServer(server);
        }
        throw new NotSupportedException(errorMessage);
    }

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
    {
        var server = await GetCallerServer();
        await CheckServerVersion(server);
        SetServerStatus(server, serverStatus, false);

        if (server.LastConfigCode.ToString() != serverStatus.ConfigCode)
        {
            _logger.LogInformation("Updating a LastConfigCode is updated ServerId: {ServerId}, ConfigCode: {ConfigCode}", server.ServerId, serverStatus.ConfigCode);
            _vhContext.Attach(server);
            server.LastConfigError = null;
            server.LastConfigCode = serverStatus.ConfigCode != null ? Guid.Parse(serverStatus.ConfigCode) : null;
            await _vhContext.SaveChangesAsync();
            _cacheRepo.UpdateServer(server);
        }

        var ret = new ServerCommand(server.ConfigCode.ToString());
        return ret;
    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
    {
        var server = await GetCallerServer();
        _logger.LogInformation("Configuring Server. ServerId: {ServerId}, Version: {Version}", server.ServerId, serverInfo.Version);
        
        // attach server for update
        _vhContext.Attach(server);
        server.Version = serverInfo.Version.ToString();

        // must after assigning version 
        await CheckServerVersion(server); 

        // update server
        server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        server.OsInfo = serverInfo.OsInfo;
        server.MachineName = serverInfo.MachineName;
        server.ConfigureTime = DateTime.UtcNow;
        server.TotalMemory = serverInfo.TotalMemory;
        server.Version = serverInfo.Version.ToString();
        server.LastConfigError = serverInfo.LastError;
        SetServerStatus(server, serverInfo.Status, true);

        // Update AccessPoints
        if (server.AccessPointGroupId != null)
            await UpdateServerAccessPoints(_vhContext, server, serverInfo);

        await _vhContext.SaveChangesAsync();
        _cacheRepo.UpdateServer(server);

        // read server accessPoints
        var accessPoints = await _vhContext.AccessPoints
            .Where(x => x.ServerId == server.ServerId && x.IsListen)
            .ToArrayAsync();

        var ipEndPoints = accessPoints
            .Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort))
            .ToArray();

        var ret = new ServerConfig(ipEndPoints, server.ConfigCode.ToString())
        {
            UpdateStatusInterval = _agentOptions.ServerUpdateStatusInterval,
            TrackingOptions = new TrackingOptions
            {
                LogClientIp = server.LogClientIp,
                LogLocalPort = server.LogLocalPort
            },
            SessionOptions = new SessionOptions
            {
                TcpBufferSize = 8192,
                SyncInterval = _agentOptions.SessionSyncInterval
            }
        };

        return ret;
    }

    private static bool AccessPointEquals(AccessPoint value1, AccessPoint value2)
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

    private static async Task UpdateServerAccessPoints(VhContext vhContext, Models.Server server, ServerInfo serverInfo)
    {
        if (server.AccessPointGroupId == null)
            throw new InvalidOperationException($"{nameof(server.AccessPointGroupId)} is not set!");

        // find current tokenAccessPoints in AccessPointGroup
        var tokenAccessPoints = await vhContext.AccessPoints.Where(x =>
                x.AccessPointGroupId == server.AccessPointGroupId &&
                x.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArrayAsync();

        var accessPoints = new List<AccessPoint>();

        // create private addresses
        foreach (var ipAddress in serverInfo.PrivateIpAddresses.Distinct())
        {
            if (serverInfo.PublicIpAddresses.Any(x => x.Equals(ipAddress)))
                continue; // will added by public address as listener

            var accessPoint = new AccessPoint
            {
                AccessPointId = Guid.NewGuid(),
                ServerId = server.ServerId,
                AccessPointGroupId = server.AccessPointGroupId.Value,
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                IpAddress = ipAddress.ToString(),
                TcpPort = 443,
                UdpPort = 0
            };
            accessPoints.Add(accessPoint);
        }

        // create public addresses
        accessPoints.AddRange(serverInfo.PublicIpAddresses
            .Distinct()
            .Select(ipAddress => new AccessPoint
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
        var curAccessPoints = server.AccessPoints?.ToArray() ?? Array.Empty<AccessPoint>();
        vhContext.AccessPoints.RemoveRange(curAccessPoints.Where(x => !accessPoints.Any(y => AccessPointEquals(x, y))));
        await vhContext.AccessPoints.AddRangeAsync(accessPoints.Where(x =>
            !curAccessPoints.Any(y => AccessPointEquals(x, y))));
    }

    private static void SetServerStatus(Models.Server server, ServerStatus serverStatus, bool isConfigure)
    {
        var serverStatusEx = new ServerStatusEx
        {
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            IsConfigure = isConfigure,
            IsLast = true,
            CreatedTime = DateTime.UtcNow,
            FreeMemory = serverStatus.FreeMemory,
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

