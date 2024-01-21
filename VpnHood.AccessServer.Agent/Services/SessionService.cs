using System.Net;
using System.Security.Authentication;
using Ga4.Ga4Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Utils;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class SessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly CacheService _cacheService;
    private readonly AgentOptions _agentOptions;
    private readonly IMemoryCache _memoryCache;
    private readonly VhContext _vhContext;

    public SessionService(
        ILogger<SessionService> logger,
        IOptions<AgentOptions> agentOptions,
        IMemoryCache memoryCache,
        CacheService cacheService,
        VhContext vhContext)
    {
        _logger = logger;
        _cacheService = cacheService;
        _agentOptions = agentOptions.Value;
        _memoryCache = memoryCache;
        _vhContext = vhContext;
    }

    private static Task TrackUsage(ulong sessionId, ServerModel server, AccessTokenModel accessToken, DeviceModel device, Traffic traffic)
    {
        var project = server.Project;
        if (project == null)
            return Task.CompletedTask;

        if (string.IsNullOrEmpty(project.GaMeasurementId) || string.IsNullOrEmpty(project.GaApiSecret))
            return Task.CompletedTask;

        var ga4Tracker = new Ga4Tracker
        {
            MeasurementId = project.GaMeasurementId,
            ApiSecret = project.GaApiSecret,
            UserAgent = device.UserAgent ?? "",
            ClientId = device.ClientId.ToString(),
            SessionId = sessionId.ToString(),
            UserId = accessToken.AccessTokenId.ToString(),
            SessionCount = 1
        };

        var ga4Event = new Ga4TagEvent
        {
            EventName = Ga4TagEvents.PageView,
            DocumentLocation = $"{server.ServerFarm?.ServerFarmName}/{server.ServerName}",
            DocumentTitle = $"{server.ServerFarm?.ServerFarmName}",
            Properties = new Dictionary<string, object>()
            {
                {"DeviceId", device.DeviceId},
                {"TokenId", accessToken.AccessTokenId},
                {"TokenCode", accessToken.SupportCode},
                {"ServerId", server.ServerId},
                {"FarmId", server.ServerFarmId},
                {"Traffic", Math.Round(traffic.Total / 1_000_000d)},
                {"Sent", Math.Round(traffic.Sent / 1_000_000d)},
                {"Received", Math.Round(traffic.Received / 1_000_000d)},
            }
        };

        if (!string.IsNullOrEmpty(accessToken.AccessTokenName)) ga4Event.Properties.Add("TokenName", accessToken.AccessTokenName);
        if (!string.IsNullOrEmpty(server.ServerName)) ga4Event.Properties.Add("ServerName", server.ServerName);
        if (!string.IsNullOrEmpty(server.ServerFarm?.ServerFarmName)) ga4Event.Properties.Add("FarmName", server.ServerFarm.ServerFarmName);

        return ga4Tracker.Track(ga4Event);
    }

    private static bool ValidateTokenRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = VhUtil.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public async Task<SessionResponseEx> CreateSession(ServerModel server, SessionRequestEx sessionRequestEx)
    {
        // validate argument
        if (server.AccessPoints == null)
            throw new ArgumentException("AccessPoints is not loaded for this model.", nameof(server));

        // extract required data
        var projectId = server.ProjectId;
        var serverId = server.ServerId;
        var clientIp = sessionRequestEx.ClientIp;
        var clientInfo = sessionRequestEx.ClientInfo;
        var requestEndPoint = sessionRequestEx.HostEndPoint;
        var accessedTime = DateTime.UtcNow;

        // Get accessTokenModel and check projectId
        var accessToken = await _vhContext.AccessTokens
            .Include(x => x.ServerFarm)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.AccessTokenId == Guid.Parse(sessionRequestEx.TokenId));

        // validate the request
        if (!ValidateTokenRequest(sessionRequestEx, accessToken.Secret))
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                ErrorMessage = "Could not validate the request."
            };

        // can serverModel request this endpoint?
        if (accessToken.ServerFarmId != server.ServerFarmId)
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                ErrorMessage = "Token does not belong to server farm."
            };

        // check is token locked
        if (!accessToken.IsEnabled)
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };

        // set accessTokenModel expiration time on first use
        if (accessToken.Lifetime != 0 && accessToken.FirstUsedTime != null &&
            accessToken.FirstUsedTime + TimeSpan.FromDays(accessToken.Lifetime) < DateTime.UtcNow)
        {
            return new SessionResponseEx(SessionErrorCode.AccessExpired)
            {
                ErrorMessage = "Your access has been expired! Please contact the support."
            };
        }

        // check is Ip Locked
        if (clientIp != null && await _vhContext.IpLocks.AnyAsync(x => x.ProjectId == projectId && x.IpAddress == clientIp.ToString() && x.LockedTime != null))
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };

        // create client or update if changed
        var clientIpToStore = clientIp != null ? IPAddressUtil.Anonymize(clientIp).ToString() : null;
        var device = await _vhContext.Devices.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientId == clientInfo.ClientId);
        if (device == null)
        {
            device = new DeviceModel(Guid.NewGuid())
            {
                ProjectId = projectId,
                ClientId = clientInfo.ClientId,
                IpAddress = clientIpToStore,
                ClientVersion = clientInfo.ClientVersion,
                UserAgent = clientInfo.UserAgent,
                CreatedTime = DateTime.UtcNow,
                ModifiedTime = DateTime.UtcNow
            };
            await _vhContext.Devices.AddAsync(device);
        }
        else
        {
            device.UserAgent = clientInfo.UserAgent;
            device.ClientVersion = clientInfo.ClientVersion;
            device.ModifiedTime = DateTime.UtcNow;
            device.IpAddress = clientIpToStore;
        }

        // check has device Locked
        if (device.LockedTime != null)
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };

        // multiple requests may be already queued through lock request until first session is created
        Guid? deviceId = accessToken.IsPublic ? device.DeviceId : null;
        using var accessLock = await GrayMint.Common.Utils.AsyncLock.LockAsync($"CreateSession_AccessId_{accessToken.AccessTokenId}_{deviceId}");
        var access = await _cacheService.GetAccessByTokenId(accessToken.AccessTokenId, deviceId);
        if (access != null) accessLock.Dispose();

        // Update or Create Access
        if (access == null)
        {
            access = new AccessModel
            {
                AccessId = Guid.NewGuid(),
                AccessTokenId = accessToken.AccessTokenId,
                DeviceId = deviceId,
                CreatedTime = DateTime.UtcNow,
                LastUsedTime = DateTime.UtcNow,
            };

            _logger.LogInformation($"New Access has been activated! AccessId: {access.AccessId}.");
            await _vhContext.Accesses.AddAsync(access);
        }

        // set access time
        access.AccessToken = accessToken;
        access.LastUsedTime = DateTime.UtcNow;

        // check supported version
        if (string.IsNullOrEmpty(clientInfo.ClientVersion) || Version.Parse(clientInfo.ClientVersion).CompareTo(ServerUtil.MinClientVersion) < 0)
            return new SessionResponseEx(SessionErrorCode.UnsupportedClient) { ErrorMessage = "This version is not supported! You need to update your app." };

        // Check Redirect to another server if everything was ok
        var bestTcpEndPoint = await FindBestServerForDevice(server, requestEndPoint, accessToken.ServerFarmId, device.DeviceId);
        if (bestTcpEndPoint == null)
            return new SessionResponseEx(SessionErrorCode.AccessError) { ErrorMessage = "Could not find any free server!" };

        // redirect if current server does not serve the best TcpEndPoint
        if (!server.AccessPoints.Any(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort).Equals(bestTcpEndPoint)))
            return new SessionResponseEx(SessionErrorCode.RedirectHost) { RedirectHostEndPoint = bestTcpEndPoint };

        // create session
        var session = new SessionModel
        {
            ProjectId = device.ProjectId,
            SessionKey = VhUtil.GenerateKey(),
            CreatedTime = DateTime.UtcNow,
            LastUsedTime = DateTime.UtcNow,
            AccessId = access.AccessId,
            DeviceIp = clientIpToStore,
            DeviceId = device.DeviceId,
            ClientVersion = device.ClientVersion,
            EndTime = null,
            ServerId = serverId,
            ExtraData = sessionRequestEx.ExtraData,
            SuppressedBy = SessionSuppressType.None,
            SuppressedTo = SessionSuppressType.None,
            ErrorCode = SessionErrorCode.Ok,
            ErrorMessage = null,

            Access = access,
            Device = device,
        };

        var ret = await BuildSessionResponse(session, accessedTime);
        ret.ExtraData = session.ExtraData;
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // update AccessToken
        accessToken.FirstUsedTime ??= session.CreatedTime;
        accessToken.LastUsedTime = session.CreatedTime;
        ret.GaMeasurementId = server.Project?.GaMeasurementId;
        ret.TcpEndPoints = new[] { bestTcpEndPoint };
        ret.UdpEndPoints = server.AccessPoints
            .Where(x => x is { IsPublic: true, UdpPort: > 0 })
            .Select(x => new IPEndPoint(x.IpAddress, x.UdpPort))
            .ToArray();

        // Add session
        session.Access = null;
        session.Device = null;
        await _vhContext.Sessions.AddAsync(session);
        await _vhContext.SaveChangesAsync();

        session.Access = access;
        session.Device = device;
        await _cacheService.AddSession(session);
        _logger.LogInformation(AccessEventId.Session, "New Session has been created. SessionId: {SessionId}", session.SessionId);

        ret.SessionId = (uint)session.SessionId;
        return ret;
    }

    public async Task<SessionResponseEx> GetSession(ServerModel server, uint sessionId, string hostEndPoint, string? clientIp)
    {
        _ = clientIp; //we don't use it now
        _ = hostEndPoint; //we don't use it now

        try
        {
            var session = await _cacheService.GetSession(server.ServerId, sessionId);

            // build response
            var ret = await BuildSessionResponse(session, DateTime.UtcNow);
            ret.ExtraData = session.ExtraData;
            _logger.LogInformation(AccessEventId.Session,
                "Reporting a session. SessionId: {SessionId}, EndTime: {EndTime}", sessionId, session.EndTime);
            return ret;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning(AccessEventId.Session,
                "Server requested a session that does not exists SessionId: {SessionId}, ServerId: {ServerId}",
                sessionId, server.ServerId);
            throw;
        }
    }

    private async Task<SessionResponseEx> BuildSessionResponse(SessionModel session, DateTime accessTime)
    {
        var access = session.Access!;
        var accessToken = session.Access!.AccessToken!;

        // update session
        access.LastUsedTime = accessTime;
        session.LastUsedTime = accessTime;

        // create common accessUsage
        var accessUsage = new AccessUsage
        {
            Traffic = new Traffic
            {
                Sent = access.TotalSentTraffic - access.LastCycleSentTraffic,
                Received = access.TotalReceivedTraffic - access.LastCycleReceivedTraffic,
            },
            MaxClientCount = accessToken.MaxDevice,
            MaxTraffic = accessToken.MaxTraffic,
            ExpirationTime = accessToken.ExpirationTime,
            ActiveClientCount = 0
        };

        // validate session status
        if (session.ErrorCode == SessionErrorCode.Ok)
        {
            // check token expiration
            if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx(SessionErrorCode.AccessExpired)
                { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

            // check traffic
            if (accessUsage.MaxTraffic != 0 &&
                accessUsage.Traffic.Total > accessUsage.MaxTraffic)
                return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

            var otherSessions = await _cacheService.GetActiveSessions(session.AccessId);

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
            if (accessUsage.MaxClientCount != 0)
            {
                var otherSessions2 = otherSessions
                    .Where(x => x.DeviceId != session.DeviceId && x.SessionId != session.SessionId)
                    .OrderBy(x => x.CreatedTime).ToArray();
                for (var i = 0; i <= otherSessions2.Length - accessUsage.MaxClientCount; i++)
                {
                    var otherSession = otherSessions2[i];
                    otherSession.SuppressedBy = SessionSuppressType.Other;
                    otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    otherSession.EndTime = DateTime.UtcNow;
                    session.SuppressedTo = SessionSuppressType.Other;
                }
            }

            accessUsage.ActiveClientCount = accessToken.IsPublic ? 0 : otherSessions.Count(x => x.EndTime == null);
        }

        // build result
        return new SessionResponseEx(session.ErrorCode)
        {
            SessionId = (uint)session.SessionId,
            CreatedTime = session.CreatedTime,
            SessionKey = session.SessionKey,
            SuppressedTo = session.SuppressedTo,
            SuppressedBy = session.SuppressedBy,
            ErrorMessage = session.ErrorMessage,
            AccessUsage = accessUsage,
            RedirectHostEndPoint = null
        };
    }

    public async Task<SessionResponseBase> AddUsage(ServerModel server, uint sessionId, Traffic traffic, bool closeSession)
    {
        var session = await _cacheService.GetSession(server.ServerId, sessionId);
        var access = session.Access ?? throw new Exception($"Could not find access. SessionId: {session.SessionId}");
        var accessToken = session.Access?.AccessToken ?? throw new Exception("AccessTokenModel is not loaded by cache.");
        var accessedTime = DateTime.UtcNow;

        // check projectId
        if (accessToken.ProjectId != server.ProjectId)
            throw new AuthenticationException();

        // update access if session is open
        _logger.LogInformation(AccessEventId.AddUsage,
            "AddUsage to a session. SessionId: {SessionId}, " +
            "SentTraffic: {SendTraffic} Bytes, ReceivedTraffic: {ReceivedTraffic} Bytes, Total: {Total}, " +
            "EndTime: {EndTime}.",
            sessionId, traffic.Sent, traffic.Received, VhUtil.FormatBytes(traffic.Total), session.EndTime);

        // add usage to access
        access.TotalReceivedTraffic += traffic.Received;
        access.TotalSentTraffic += traffic.Sent;
        access.LastUsedTime = DateTime.UtcNow;

        // insert AccessUsageLog
        _cacheService.AddSessionUsage(new AccessUsageModel
        {
            ReceivedTraffic = traffic.Received,
            SentTraffic = traffic.Sent,
            TotalReceivedTraffic = access.TotalReceivedTraffic,
            TotalSentTraffic = access.TotalSentTraffic,
            DeviceId = session.DeviceId,
            LastCycleReceivedTraffic = access.LastCycleReceivedTraffic,
            LastCycleSentTraffic = access.LastCycleSentTraffic,
            CreatedTime = DateTime.UtcNow,
            AccessId = session.AccessId,
            SessionId = session.SessionId,
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            AccessTokenId = accessToken.AccessTokenId,
            ServerFarmId = accessToken.ServerFarmId,
        });

        _ = TrackUsage(sessionId, server, accessToken, session.Device!, traffic);

        // build response
        var sessionResponse = await BuildSessionResponse(session, accessedTime);

        // close session
        if (closeSession)
        {
            _logger.LogInformation(AccessEventId.Session,
                "Session has been closed by user. SessionId: {SessionId}, ResponseCode: {ResponseCode}",
                sessionId, sessionResponse.ErrorCode);

            if (sessionResponse.ErrorCode == SessionErrorCode.Ok)
                session.ErrorCode = SessionErrorCode.SessionClosed;

            session.EndTime ??= DateTime.UtcNow;
        }

        return new SessionResponseBase(sessionResponse);
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(ServerModel currentServer, IPEndPoint currentEndPoint, Guid serverFarmId, Guid deviceId)
    {
        // prevent re-redirect if device has already redirected to this serverModel
        var cacheKey = $"LastDeviceServer/{serverFarmId}/{deviceId}";
        if (!_agentOptions.AllowRedirect ||
            (_memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) && lastDeviceServerId == currentServer.ServerId))
        {
            if (IsServerReady(currentServer))
                return currentEndPoint;
        }

        // get all servers of this farm
        var servers = await _cacheService.GetServers();
        var farmServers = servers.Values
            .Where(server =>
                server.ProjectId == currentServer.ProjectId &&
                server.ServerFarmId == currentServer.ServerFarmId &&
                server.AccessPoints.Any(accessPoint =>
                    accessPoint.IsPublic && accessPoint.IpAddress.AddressFamily == currentEndPoint.AddressFamily) &&
                IsServerReady(server))
            .ToArray();

        // find the best free server
        var bestServer = farmServers
            .MinBy(server => server.ServerStatus!.SessionCount);

        if (bestServer != null)
        {
            _memoryCache.Set(cacheKey, bestServer.ServerId, TimeSpan.FromMinutes(5));
            var serverEndPoint = bestServer.AccessPoints.First(accessPoint =>
                accessPoint.IsPublic && accessPoint.IpAddress.AddressFamily == currentEndPoint.AddressFamily);
            var ret = new IPEndPoint(serverEndPoint.IpAddress, serverEndPoint.TcpPort);
            return ret;
        }

        return null;
    }

    private bool IsServerReady(ServerModel currentServerModel)
    {
        return ServerUtil.IsServerReady(currentServerModel, _agentOptions.LostServerThreshold);
    }
}
