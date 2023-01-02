using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;

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

    private static async Task TrackSession(ServerModel serverModel, DeviceModel device, string accessPointGroupName, string accessTokenName)
    {
        var project = serverModel.Project;
        if (string.IsNullOrEmpty(project?.GaTrackId))
            return;

        var analyticsTracker = new GoogleAnalyticsTracker(project.GaTrackId, device.DeviceId.ToString(),
            "VpnHoodService", device.ClientVersion ?? "1", device.UserAgent)
        {
            IpAddress = device.IpAddress != null && IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null,
        };

        var trackData = new TrackData($"{accessPointGroupName}/{accessTokenName}", accessTokenName);
        await analyticsTracker.Track(trackData);
    }

    private static async Task TrackUsage(ServerModel serverModel, AccessTokenModel accessTokenModel, DeviceModel device, UsageInfo usageInfo)
    {
        var project = serverModel.Project;
        if (string.IsNullOrEmpty(project?.GaTrackId))
            return;

        var analyticsTracker = new GoogleAnalyticsTracker(project.GaTrackId, device.DeviceId.ToString(),
            "VpnHoodService", device.ClientVersion ?? "1", device.UserAgent)
        {
            IpAddress = device.IpAddress != null && IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null
        };

        var traffic = (usageInfo.SentTraffic + usageInfo.ReceivedTraffic) * 2 / 1000000;
        var accessTokenName = string.IsNullOrEmpty(accessTokenModel.AccessTokenName) ? accessTokenModel.AccessTokenId.ToString() : accessTokenModel.AccessTokenName;
        var groupName = accessTokenModel.AccessPointGroup?.AccessPointGroupName ?? accessTokenModel.AccessPointGroupId.ToString();
        var serverName = string.IsNullOrEmpty(serverModel.ServerName) ? serverModel.ServerId.ToString() : serverModel.ServerName;
        var trackDatas = new TrackData[]
        {
            new ("Usage", "GroupUsage", groupName, traffic),
            new ("Usage", "AccessTokenModel", accessTokenName, traffic),
            new ("Usage", "ServerUsage", serverName, traffic),
            new ("Usage", "Device", device.DeviceId.ToString(), traffic)
        };

        await analyticsTracker.Track(trackDatas);
    }

    private static bool ValidateTokenRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
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
            .Include(x => x.AccessPointGroup)
            .SingleAsync(x => x.AccessTokenId == sessionRequestEx.TokenId && x.ProjectId == projectId);

        // validate the request
        if (!ValidateTokenRequest(sessionRequestEx, accessToken.Secret))
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                ErrorMessage = "Could not validate the request."
            };

        // can serverModel request this endpoint?
        if (!ValidateServerEndPoint(server, requestEndPoint, accessToken.AccessPointGroupId))
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                ErrorMessage = "Invalid EndPoint request."
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

        // multiple requests may queued through lock request until first session is created
        Guid? deviceId = accessToken.IsPublic ? device.DeviceId : null;
        using var accessLock = await AsyncLock.LockAsync($"CreateSession_AccessId_{accessToken.AccessTokenId}_{deviceId}");
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

        // create session
        var session = new SessionModel
        {
            SessionKey = Util.GenerateSessionKey(),
            CreatedTime = DateTime.UtcNow,
            LastUsedTime = DateTime.UtcNow,
            AccessId = access.AccessId,
            DeviceIp = clientIpToStore,
            DeviceId = device.DeviceId,
            ClientVersion = device.ClientVersion,
            EndTime = null,
            ServerId = serverId,
            SuppressedBy = SessionSuppressType.None,
            SuppressedTo = SessionSuppressType.None,
            ErrorCode = SessionErrorCode.Ok,
            ErrorMessage = null,

            Access = access,
            Device = device,
        };

        var ret = await BuildSessionResponse(session, accessedTime);
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // check supported version
        var minSupportedVersion = Version.Parse("2.3.289");
        if (string.IsNullOrEmpty(clientInfo.ClientVersion) || Version.Parse(clientInfo.ClientVersion).CompareTo(minSupportedVersion) < 0)
            return new SessionResponseEx(SessionErrorCode.UnsupportedClient) { ErrorMessage = "This version is not supported! You need to update your app." };

        // Check Redirect to another serverModel if everything was ok
        var bestEndPoint = await FindBestServerForDevice(server, requestEndPoint, accessToken.AccessPointGroupId, device.DeviceId);
        if (bestEndPoint == null)
            return new SessionResponseEx(SessionErrorCode.AccessError) { ErrorMessage = "Could not find any free server!" };

        if (!bestEndPoint.Equals(requestEndPoint))
            return new SessionResponseEx(SessionErrorCode.RedirectHost) { RedirectHostEndPoint = bestEndPoint };

        // update AccessToken
        accessToken.FirstUsedTime ??= session.CreatedTime;
        accessToken.LastUsedTime = session.CreatedTime;

        // Add session
        session.Access = null;
        session.Device = null;
        await _vhContext.Sessions.AddAsync(session);
        await _vhContext.SaveChangesAsync();

        session.Access = access;
        session.Device = device;
        await _cacheService.AddSession(session);
        _logger.LogInformation(AccessEventId.Session, "New Session has been created. SessionId: {SessionId}", session.SessionId);

        _ = TrackSession(server, device, accessToken.AccessPointGroup!.AccessPointGroupName ?? "Group-" + accessToken.AccessPointGroupId, accessToken.AccessTokenName ?? "token-" + accessToken.AccessTokenId);
        ret.SessionId = (uint)session.SessionId;
        return ret;
    }

    private static bool ValidateServerEndPoint(ServerModel serverModel, IPEndPoint requestEndPoint, Guid accessPointGroupId)
    {
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        // validate request to this serverModel
        var ret = serverModel.AccessPoints!.Any(x =>
            x.TcpPort == requestEndPoint.Port &&
            x.AccessPointGroupId == accessPointGroupId &&
            (x.IpAddress == anyIp.ToString() || x.IpAddress == requestEndPoint.Address.ToString())
        );

        return ret;
    }

    public async Task<SessionResponseEx> GetSession(ServerModel serverModel, uint sessionId, string hostEndPoint, string? clientIp)
    {
        // validate argument
        if (serverModel.AccessPoints == null)
            throw new ArgumentException("AccessPoints is not loaded for this model.", nameof(serverModel));

        _ = clientIp; //we don't use it now
        try
        {
            var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
            var session = await _cacheService.GetSession(serverModel.ServerId, sessionId);
            var accessToken = session.Access!.AccessToken!;

            // can serverModel request this endpoint?
            if (!ValidateServerEndPoint(serverModel, requestEndPoint, accessToken.AccessPointGroupId))
                return new SessionResponseEx(SessionErrorCode.AccessError)
                {
                    ErrorMessage = "Invalid EndPoint request!"
                };

            // build response
            var ret = await BuildSessionResponse(session, DateTime.UtcNow);
            _logger.LogInformation(AccessEventId.Session,
                "Reporting a session. SessionId: {SessionId}, EndTime: {EndTime}", sessionId, session.EndTime);
            return ret;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning(AccessEventId.Session,
                "Server requested a session that does not exists SessionId: {SessionId}, ServerId: {ServerId}", 
                sessionId, serverModel.ServerId);
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
            MaxClientCount = accessToken.MaxDevice,
            MaxTraffic = accessToken.MaxTraffic,
            ExpirationTime = accessToken.ExpirationTime,
            SentTraffic = access.TotalSentTraffic - access.LastCycleSentTraffic,
            ReceivedTraffic = access.TotalReceivedTraffic - access.LastCycleReceivedTraffic,
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
                accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessUsage.MaxTraffic)
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

    public async Task<ResponseBase> AddUsage(ServerModel serverModel, uint sessionId, UsageInfo usageInfo, bool closeSession)
    {
        // temp for server bug
        // LogWarning should be reported
        SessionModel session;
        try { session = await _cacheService.GetSession(serverModel.ServerId, sessionId); }
        catch
        {
            _logger.LogWarning(AccessEventId.Session,
                "VpnServer tries to add usage to a session usage that does not exists. SessionId: {SessionId}, ServerId: {ServerId}",
                sessionId, serverModel.ServerId);

            // todo: temporary for servers less or equal than v2.4.321
            return new ResponseBase(SessionErrorCode.SessionClosed);
        }

        var access = session.Access ?? throw new Exception($"Could not find access. SessionId: {session.SessionId}");
        var accessToken = session.Access?.AccessToken ?? throw new Exception("AccessTokenModel is not loaded by cache.");
        var accessedTime = DateTime.UtcNow;
        
        // check projectId
        if (accessToken.ProjectId != serverModel.ProjectId)
            throw new AuthenticationException();

        // update access if session is open
        _logger.LogInformation(AccessEventId.AddUsage,
            "AddUsage to a session. SessionId: {SessionId}, " +
            "SentTraffic: {SendTraffic} Bytes, ReceivedTraffic: {ReceivedTraffic} Bytes, Total: {Total}, " +
            "EndTime: {EndTime}.",
            sessionId, usageInfo.SentTraffic, usageInfo.ReceivedTraffic, Util.FormatBytes(usageInfo.SentTraffic + usageInfo.ReceivedTraffic), session.EndTime);

        // add usage to access
        access.TotalReceivedTraffic += usageInfo.ReceivedTraffic;
        access.TotalSentTraffic += usageInfo.SentTraffic;
        access.LastUsedTime = DateTime.UtcNow;

        // insert AccessUsageLog
        _cacheService.AddSessionUsage(new AccessUsageModel
        {
            ReceivedTraffic = usageInfo.ReceivedTraffic,
            SentTraffic = usageInfo.SentTraffic,
            TotalReceivedTraffic = access.TotalReceivedTraffic,
            TotalSentTraffic = access.TotalSentTraffic,
            DeviceId = session.DeviceId,
            LastCycleReceivedTraffic = access.LastCycleReceivedTraffic,
            LastCycleSentTraffic = access.LastCycleSentTraffic,
            CreatedTime = DateTime.UtcNow,
            AccessId = session.AccessId,
            SessionId = session.SessionId,
            ProjectId = serverModel.ProjectId,
            ServerId = serverModel.ServerId,
            AccessTokenId = accessToken.AccessTokenId,
            AccessPointGroupId = accessToken.AccessPointGroupId,
        });

        _ = TrackUsage(serverModel, accessToken, session.Device!, usageInfo);

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

        return new ResponseBase(sessionResponse);
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(ServerModel currentServerModel, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
    {
        // prevent re-redirect if device has already redirected to this serverModel
        var cacheKey = $"LastDeviceServer/{deviceId}";
        if (!_agentOptions.AllowRedirect ||
            (_memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) && lastDeviceServerId == currentServerModel.ServerId))
        {
            if (IsServerReady(currentServerModel))
                return currentEndPoint;
        }

        // get all servers of this farm
        var servers = (await _cacheService.GetServers()).Values.ToArray();
        servers = servers.Where(server => server.ProjectId == currentServerModel.ProjectId && IsServerReady(server)).ToArray();

        // find all accessPoints belong to this farm
        var accessPoints = new List<AccessPointModel>();
        foreach (var server in servers)
            foreach (var accessPoint in server.AccessPoints!.Where(x =>
                         x.AccessPointGroupId == accessPointGroupId &&
                         x.AccessPointMode is AccessPointMode.PublicInToken or AccessPointMode.Public &&
                         IPAddress.Parse(x.IpAddress).AddressFamily == currentEndPoint.AddressFamily))
            {
                accessPoint.Server = server;
                accessPoints.Add(accessPoint);
            }

        // find the best free serverModel
        var best = accessPoints
            .GroupBy(x => x.ServerId)
            .Select(x => x.First())
            .MinBy(x => x.Server!.ServerStatus!.SessionCount);

        if (best != null)
        {
            _memoryCache.Set(cacheKey, best.ServerId, TimeSpan.FromMinutes(5));
            var ret = new IPEndPoint(IPAddress.Parse(best.IpAddress), best.TcpPort);
            return ret;
        }

        return null;
    }

    private bool IsServerReady(ServerModel currentServerModel)
    {
        return ServerUtil.IsServerReady(currentServerModel, _agentOptions.LostServerThreshold);
    }
}
