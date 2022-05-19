using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/agent")]
[Authorize(AuthenticationSchemes = AppOptions.AuthRobotScheme)]
public class AgentController : ControllerBase
{
    private readonly SessionManager _sessionManager;
    private readonly IOptions<AppOptions> _appOptions;
    private readonly ILogger<AgentController> _logger;
    private readonly VhContext _vhContext;
    private readonly SystemCache _systemCache;

    public AgentController(ILogger<AgentController> logger, VhContext vhContext,
        SessionManager sessionManager,
        SystemCache systemCache,
        IOptions<AppOptions> appOptions)
    {
        _systemCache = systemCache;
        _appOptions = appOptions;
        _sessionManager = sessionManager;
        _logger = logger;
        _vhContext = vhContext;
    }

    private async Task<Models.Server> GetCallerServer()
    {
        // find serverId from identity claims
        var subject = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ??
                      throw new UnauthorizedAccessException();
        if (!Guid.TryParse(subject, out var serverId))
            throw new UnauthorizedAccessException();

        // find authorizationCode from identity claims
        var authorizationCodeStr = User.Claims.FirstOrDefault(claim => claim.Type == "authorization_code")?.Value;
        if (!Guid.TryParse(authorizationCodeStr, out var authorizationCode))
            throw new UnauthorizedAccessException();

        try
        {
            var server = await _systemCache.GetServer(_vhContext, serverId);
            if (server.AuthorizationCode != authorizationCode)
                throw new UnauthorizedAccessException();
            return server;
        }
        catch (KeyNotFoundException)
        {
            throw new UnauthorizedAccessException();
        }

    }

    [HttpPost("sessions")]
    public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var server = await GetCallerServer();
        return await _sessionManager.CreateSession(sessionRequestEx, _vhContext, server);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> Session_Get(uint sessionId, string hostEndPoint, string? clientIp)
    {
        var server = await GetCallerServer();
        return await _sessionManager.GetSession(sessionId, hostEndPoint, clientIp, _vhContext, server);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<ResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo, bool closeSession = false)
    {
        var server = await GetCallerServer();

        // find serverId from identity claims
        //var subject = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
        //if (!Guid.TryParse(subject, out var serverId))
        //    throw new UnauthorizedAccessException();
        //_logger.LogInformation($"Session_AddUsage, Server: {server.ServerId}, {sessionId}");
        //return new ResponseBase(SessionErrorCode.Ok)
        //{
        //    AccessUsage = new AccessUsage(),
        //};

        return await _sessionManager.AddUsage(sessionId, usageInfo, closeSession, _vhContext, server);
    }

    [HttpGet("certificates/{hostEndPoint}")]
    public async Task<byte[]> GetSslCertificateData(string hostEndPoint)
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

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
    {
        var server = await GetCallerServer();
        SetServerStatus(server, serverStatus, false);
        
        var isLegacy = Version.Parse(server.Version!) <= Version.Parse("2.4.300");
        if (!server.IsConfigured && (isLegacy || server.ConfigCode.ToString() == serverStatus.ConfigCode))
        {
            _vhContext.Attach(server);
            server.IsConfigured = true;
            await _vhContext.SaveChangesAsync();
        }

        var configCode = isLegacy ? null! : server.ConfigCode.ToString();
        var ret = new ServerCommand(configCode);
        return ret;
    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
    {
        var server = await GetCallerServer();
        _logger.LogInformation("Configuring Server. ServerId: {ServerId}, Version: {Version}", server.ServerId,
            serverInfo.Version);

        // we need to update the server, so prepare it for update after validate it by cache
        // Ef Core wisely update only changed fields
        server = await _vhContext.Servers
            .Include(x => x.AccessPoints)
            .SingleAsync(x => x.ServerId == server.ServerId);

        // update server
        server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        server.OsInfo = serverInfo.OsInfo;
        server.MachineName = serverInfo.MachineName;
        server.ConfigureTime = DateTime.UtcNow;
        server.TotalMemory = serverInfo.TotalMemory;
        server.Version = serverInfo.Version.ToString();
        server.IsConfigured = false;
        SetServerStatus(server, serverInfo.Status, true);

        // check is Access
        if (server.AccessPointGroupId != null)
            await UpdateServerAccessPoints(_vhContext, server, serverInfo);

        await _vhContext.SaveChangesAsync();
        _systemCache.UpdateServer(server);

        // read server accessPoints
        var accessPoints = await _vhContext.AccessPoints
            .Where(x => x.ServerId == server.ServerId && x.IsListen)
            .ToArrayAsync();

        var ipEndPoints = accessPoints
            .Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort))
            .ToArray();

        var ret = new ServerConfig(ipEndPoints, server.ConfigCode.ToString())
        {
            UpdateStatusInterval = _appOptions.Value.ServerUpdateStatusInterval,
            TrackingOptions = new TrackingOptions
            {
                LogClientIp = server.LogClientIp,
                LogLocalPort = server.LogLocalPort
            },
            SessionOptions = new SessionOptions
            {
                TcpBufferSize = 8192,
                SyncInterval = TimeSpan.FromHours(24)
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

