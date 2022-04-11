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
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using Access = VpnHood.AccessServer.Models.Access;
using AccessUsageEx = VpnHood.AccessServer.Models.AccessUsageEx;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/agent")]
[Authorize(AuthenticationSchemes = AppOptions.AuthRobotScheme)]
public class AgentController : ControllerBase
{
    private readonly SessionManager _sessionManager;
    private readonly ServerManager _serverManager;
    private readonly IOptions<AppOptions> _appOptions;
    private readonly ILogger<AgentController> _logger;
    private readonly VhContext _vhContext;
    private readonly SystemCache _systemCache;

    public AgentController(ILogger<AgentController> logger, VhContext vhContext,
        ServerManager serverManager,
        SessionManager sessionManager,
        SystemCache systemCache,
        IOptions<AppOptions> appOptions)
    {
        _serverManager = serverManager;
        _systemCache = systemCache;
        _appOptions = appOptions;
        _sessionManager = sessionManager;
        _logger = logger;
        _vhContext = vhContext;
    }

    private async Task<Models.Server> GetCallerServer()
    {
        // find serverId from identity claims
        var subject = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
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

    private static bool ValidateRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    private static SessionResponseEx BuildSessionResponse(VhContext vhContext, Session session,
        AccessToken accessToken, Access access)
    {
        // create common accessUsage
        var accessUsage2 = new AccessUsage
        {
            MaxClientCount = accessToken.MaxDevice,
            MaxTraffic = accessToken.MaxTraffic,
            ExpirationTime = access.EndTime,
            SentTraffic = access.CycleSentTraffic,
            ReceivedTraffic = access.CycleReceivedTraffic,
            ActiveClientCount = 0
        };

        // validate session status
        if (session.ErrorCode == SessionErrorCode.Ok)
        {
            // check token expiration
            if (accessUsage2.ExpirationTime != null && accessUsage2.ExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx(SessionErrorCode.AccessExpired)
                { AccessUsage = accessUsage2, ErrorMessage = "Access Expired!" };

            // check traffic
            if (accessUsage2.MaxTraffic != 0 &&
                accessUsage2.SentTraffic + accessUsage2.ReceivedTraffic > accessUsage2.MaxTraffic)
                return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                { AccessUsage = accessUsage2, ErrorMessage = "All traffic quota has been consumed!" };

            var otherSessions = vhContext.Sessions
                .Where(x => x.EndTime == null && x.AccessId == session.AccessId)
                .OrderBy(x => x.CreatedTime).ToArray();

            // suppressedTo yourself
            var selfSessions = otherSessions.Where(x =>
                x.DeviceId == session.DeviceId && x.SessionId != session.SessionId).ToArray();
            if (selfSessions.Any())
            {
                session.SuppressedTo = SessionSuppressType.YourSelf;
                foreach (var selfSession in selfSessions)
                {
                    selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                    selfSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    selfSession.EndTime = DateTime.UtcNow;
                }
            }

            // suppressedTo others by MaxClientCount
            if (accessUsage2.MaxClientCount != 0)
            {
                var otherSessions2 = otherSessions
                    .Where(x => x.DeviceId != session.DeviceId && x.SessionId != session.SessionId)
                    .OrderBy(x => x.CreatedTime).ToArray();
                for (var i = 0; i <= otherSessions2.Length - accessUsage2.MaxClientCount; i++)
                {
                    var otherSession = otherSessions2[i];
                    otherSession.SuppressedBy = SessionSuppressType.Other;
                    otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    otherSession.EndTime = DateTime.UtcNow;
                    session.SuppressedTo = SessionSuppressType.Other;
                }
            }

            accessUsage2.ActiveClientCount = accessToken.IsPublic ? 0 : otherSessions.Count(x => x.EndTime == null);
        }

        // build result
        return new SessionResponseEx(SessionErrorCode.Ok)
        {
            SessionId = (uint)session.SessionId,
            CreatedTime = session.CreatedTime,
            SessionKey = session.SessionKey,
            SuppressedTo = session.SuppressedTo,
            SuppressedBy = session.SuppressedBy,
            ErrorCode = session.ErrorCode,
            ErrorMessage = session.ErrorMessage,
            AccessUsage = accessUsage2,
            RedirectHostEndPoint = null
        };
    }

    [HttpPost("sessions")]
    public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var server = await GetCallerServer();
        return await _sessionManager.Create(sessionRequestEx, _vhContext, server);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> Session_Get(uint sessionId, string hostEndPoint, string? clientIp)
    {
        _ = clientIp;
        var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        var server = await GetCallerServer();

        // make sure hostEndPoint is accessible by this session
        var query = from atg in _vhContext.AccessPointGroups
                    join at in _vhContext.AccessTokens on atg.AccessPointGroupId equals at.AccessPointGroupId
                    join a in _vhContext.Accesses on at.AccessTokenId equals a.AccessTokenId
                    join s in _vhContext.Sessions on a.AccessId equals s.AccessId
                    join accessPoint in _vhContext.AccessPoints on atg.AccessPointGroupId equals accessPoint.AccessPointGroupId
                    where at.ProjectId == server.ProjectId &&
                          accessPoint.ServerId == server.ServerId &&
                          s.SessionId == sessionId &&
                          a.AccessId == s.AccessId &&
                          accessPoint.IsListen &&
                          accessPoint.TcpPort == requestEndPoint.Port &&
                          (accessPoint.IpAddress == anyIp.ToString() || accessPoint.IpAddress == requestEndPoint.Address.ToString())
                    select new { at, a, s };
        var result = await query.SingleAsync();

        var accessToken = result.at;
        var access = result.a;
        var session = result.s;

        // build response
        var ret = BuildSessionResponse(_vhContext, session, accessToken, access);

        // update session AccessedTime
        result.s.AccessedTime = DateTime.UtcNow;
        await _vhContext.SaveChangesAsync();

        return ret;
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<ResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo, bool closeSession = false)
    {
        // find serverId from identity claims
        //var subject = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
        //if (!Guid.TryParse(subject, out var serverId))
        //    throw new UnauthorizedAccessException();
        //_logger.LogInformation($"Session_AddUsage, Server: {serverId}, {sessionId}");
        //return new ResponseBase(SessionErrorCode.Ok)
        //{
        //    AccessUsage = new AccessUsage(),
        //};

        var server = await GetCallerServer();

        // make sure hostEndPoint is accessible by this session
        var query = from at in _vhContext.AccessTokens
                    join farm in _vhContext.AccessPointGroups on at.AccessPointGroupId equals farm.AccessPointGroupId
                    join a in _vhContext.Accesses on at.AccessTokenId equals a.AccessTokenId
                    join s in _vhContext.Sessions on a.AccessId equals s.AccessId
                    join device in _vhContext.Devices on s.DeviceId equals device.DeviceId
                    where at.ProjectId == server.ProjectId && s.SessionId == sessionId && a.AccessId == s.AccessId
                    select new { at, a, s, device, farm.AccessPointGroupName };
        var result = await query.SingleAsync();

        var accessToken = result.at;
        var access = result.a;
        var session = result.s;

        // update access
        _logger.LogInformation($"AddUsage to {access.AccessId}, SentTraffic: {usageInfo.SentTraffic / 1000000} MB, ReceivedTraffic: {usageInfo.ReceivedTraffic / 1000000} MB");
        access.CycleReceivedTraffic += usageInfo.ReceivedTraffic;
        access.CycleSentTraffic += usageInfo.SentTraffic;
        access.TotalReceivedTraffic += usageInfo.ReceivedTraffic;
        access.TotalSentTraffic += usageInfo.SentTraffic;
        access.AccessedTime = DateTime.UtcNow;

        // insert AccessUsageLog
        await _vhContext.AccessUsages.AddAsync(new AccessUsageEx
        {
            AccessId = session.AccessId,
            SessionId = (uint)session.SessionId,
            ReceivedTraffic = usageInfo.ReceivedTraffic,
            SentTraffic = usageInfo.SentTraffic,
            ProjectId = server.ProjectId,
            AccessTokenId = accessToken.AccessTokenId,
            AccessPointGroupId = accessToken.AccessPointGroupId,
            DeviceId = session.DeviceId,
            CycleReceivedTraffic = access.CycleReceivedTraffic,
            CycleSentTraffic = access.CycleSentTraffic,
            TotalReceivedTraffic = access.TotalReceivedTraffic,
            TotalSentTraffic = access.TotalSentTraffic,
            ServerId = server.ServerId,
            CreatedTime = access.AccessedTime
        });
        _ = TrackUsage(server, accessToken, result.AccessPointGroupName, result.device, usageInfo);

        // build response
        var ret = BuildSessionResponse(_vhContext, session, accessToken, access);

        // close session
        if (closeSession)
        {
            if (ret.ErrorCode == SessionErrorCode.Ok)
                session.ErrorCode = SessionErrorCode.SessionClosed;
            session.EndTime ??= session.EndTime = DateTime.UtcNow;
        }

        // update session
        session.AccessedTime = DateTime.UtcNow;

        await _vhContext.SaveChangesAsync();
        return new ResponseBase(ret);
    }

    private async Task TrackUsage(Models.Server server, AccessToken accessToken, string? farmName,
        Device device, UsageInfo usageInfo)
    {
        if (server.ProjectId != Guid.Parse("8b90f69b-264f-4d4f-9d42-f614de4e3aea"))
            return;

        var analyticsTracker = new GoogleAnalyticsTracker("UA-183010362-2", device.DeviceId.ToString(),
            "VpnHoodService", device.ClientVersion ?? "1", device.UserAgent)
        {
            IpAddress = device.IpAddress != null && IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null
        };

        var traffic = (usageInfo.SentTraffic + usageInfo.ReceivedTraffic) * 2 / 1000000;
        var trackDatas = new TrackData[]
        {
            new("Usage", "FarmUsageById", accessToken.AccessPointGroupId.ToString(), traffic),
            new("Usage", "AccessTokenById", accessToken.AccessTokenId.ToString(), traffic),
            new("Usage", "ServerUsageById", server.ServerId.ToString(), traffic),
            new("Usage", "Device", device.DeviceId.ToString(), traffic),
        }.ToList();

        trackDatas.Add(new TrackData("Usage", "FarmUsage", string.IsNullOrEmpty(farmName) ? accessToken.AccessPointGroupId.ToString() : farmName, traffic));
        trackDatas.Add(new TrackData("Usage", "AccessToken", string.IsNullOrEmpty(accessToken.AccessTokenName) ? accessToken.AccessTokenId.ToString() : accessToken.AccessTokenName, traffic));
        trackDatas.Add(new TrackData("Usage", "ServerUsage", string.IsNullOrEmpty(server.ServerName) ? server.ServerId.ToString() : server.ServerName, traffic));

        await analyticsTracker.Track(trackDatas.ToArray());
    }

    private async Task TrackSession(Device device, string farmName, string accessTokenName)
    {
        if (device.ProjectId != Guid.Parse("8b90f69b-264f-4d4f-9d42-f614de4e3aea"))
            return;

        var analyticsTracker = new GoogleAnalyticsTracker("UA-183010362-2", device.DeviceId.ToString(),
            "VpnHoodService", device.ClientVersion ?? "1", device.UserAgent)
        {
            IpAddress = device.IpAddress != null && IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null,
        };

        var trackData = new TrackData($"{farmName}/{accessTokenName}", accessTokenName);
        await analyticsTracker.Track(trackData);
    }


    [HttpGet("certificates/{hostEndPoint}")]
    public async Task<byte[]> GetSslCertificateData(string hostEndPoint)
    {
        var server = await GetCallerServer();

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
                                  (x.IpAddress == anyIp.ToString() || x.IpAddress == requestEndPoint.Address.ToString()));


        return accessPoint.AccessPointGroup!.Certificate!.RawData;
    }

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
    {
        var server = await GetCallerServer();

        await InsertServerStatus(_vhContext, server, serverStatus, false);
        await _vhContext.SaveChangesAsync();

        var ret = new ServerCommand
        {
            ConfigCode = server.ConfigCode
        };

        return ret;
    }

    private static async Task InsertServerStatus(VhContext vhContext, Models.Server server,
        ServerStatus serverStatus, bool isConfigure)
    {
        var serverStatusLog = await vhContext.ServerStatuses.SingleOrDefaultAsync(x => x.ServerId == server.ServerId && x.IsLast);

        // remove IsLast
        if (serverStatusLog != null)
            serverStatusLog.IsLast = false;

        await vhContext.ServerStatuses.AddAsync(new ServerStatusEx
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
        });

    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
    {
        var server = await GetCallerServer();

        // we need to update the server, so prepare it for update after validate it by cache
        // Ef Core wisely update only changed field
        server = await _vhContext.Servers.SingleAsync(x => x.ServerId == server.ServerId);

        // update server
        server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        server.OsInfo = serverInfo.OsInfo;
        server.MachineName = serverInfo.MachineName;
        server.ConfigureTime = DateTime.UtcNow;
        server.TotalMemory = serverInfo.TotalMemory;
        server.Version = serverInfo.Version.ToString();
        if (server.ConfigCode == serverInfo.ConfigCode) server.ConfigCode = null;
        await InsertServerStatus(_vhContext, server, serverInfo.Status, true);

        // check is Access
        if (server.AccessPointGroupId != null)
            await UpdateServerAccessPoints(_vhContext, server, serverInfo);

        await _vhContext.SaveChangesAsync();
        _systemCache.InvalidateServer(server.ServerId);

        // read server accessPoints
        var accessPoints = await _vhContext.AccessPoints
            .Where(x => x.ServerId == server.ServerId && x.IsListen)
            .ToArrayAsync();

        var ipEndPoints = accessPoints.Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort)).ToArray();
        var ret = new ServerConfig(ipEndPoints)
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
        if (server.AccessPointGroupId == null) throw new InvalidOperationException($"{nameof(server.AccessPointGroupId)} is not set!");

        // find current tokenAccessPoints in AccessPointGroup
        var tokenAccessPoints = await vhContext.AccessPoints.Where(x =>
                x.AccessPointGroupId == server.AccessPointGroupId &&
                x.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArrayAsync();

        var accessPoints = new List<AccessPoint>();

        // create private addresses
        foreach (var ipAddress in serverInfo.PrivateIpAddresses)
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
                    ? AccessPointMode.PublicInToken : AccessPointMode.Public, // prefer last value
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
        await vhContext.AccessPoints.AddRangeAsync(accessPoints.Where(x => !curAccessPoints.Any(y => AccessPointEquals(x, y))));
    }
}