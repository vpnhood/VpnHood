using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.AccessServer.Utils;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Agent.Repos;

public class SessionRepo
{
    private readonly ILogger<SessionRepo> _logger;
    private readonly CacheRepo _cacheRepo;
    private readonly AgentOptions _agentOptions;
    private readonly IMemoryCache _memoryCache;
    private readonly VhContext _vhContext;

    public SessionRepo(
        ILogger<SessionRepo> logger,
        IOptions<AgentOptions> agentOptions,
        IMemoryCache memoryCache,
        CacheRepo cacheRepo,
        VhContext vhContext)
    {
        _logger = logger;
        _cacheRepo = cacheRepo;
        _agentOptions = agentOptions.Value;
        _memoryCache = memoryCache;
        _vhContext = vhContext;
    }

    private static async Task TrackSession(Device device, string farmName, string accessTokenName)
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

    private static async Task TrackUsage(Models.Server server, AccessToken accessToken, string? farmName,
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

    private static bool ValidateTokenRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public async Task<SessionResponseEx> CreateSession(SessionRequestEx sessionRequestEx, Models.Server server)
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

        // Get accessToken and check projectId
        var accessToken = await _vhContext.AccessTokens
            .Include(x => x.AccessPointGroup)
            .SingleAsync(x => x.AccessTokenId == sessionRequestEx.TokenId && x.ProjectId == projectId);

        // validate the request
        if (!ValidateTokenRequest(sessionRequestEx, accessToken.Secret))
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Could not validate the request!"
            };

        // can server request this endpoint?
        if (!ValidateServerEndPoint(server, requestEndPoint, accessToken.AccessPointGroupId))
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Invalid EndPoint request!"
            };

        // check has Ip Locked
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
            device = new Device(Guid.NewGuid())
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
        var access = await _cacheRepo.GetAccessByTokenId(accessToken.AccessTokenId, deviceId);
        if (access != null) accessLock.Dispose();

        // Update or Create Access
        var isNewAccess = access == null;
        access ??= new Access(Guid.NewGuid())
        {
            AccessTokenId = sessionRequestEx.TokenId,
            DeviceId = deviceId,
            CreatedTime = DateTime.UtcNow,
            EndTime = accessToken.EndTime,
        };

        // set access time
        access.AccessToken = accessToken;
        access.AccessedTime = DateTime.UtcNow;

        // set accessToken expiration time on first use
        if (access.EndTime == null && accessToken.Lifetime != 0)
            access.EndTime = DateTime.UtcNow.AddDays(accessToken.Lifetime);

        if (isNewAccess)
        {
            _logger.LogInformation($"Access has been activated! AccessId: {access.AccessId}");
            await _vhContext.Accesses.AddAsync(access);
        }

        // create session
        var session = new Session
        {
            SessionKey = Util.GenerateSessionKey(),
            CreatedTime = DateTime.UtcNow,
            AccessedTime = DateTime.UtcNow,
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

        // Check Redirect to another server if everything was ok
        var bestEndPoint = await FindBestServerForDevice(server, requestEndPoint, accessToken.AccessPointGroupId, device.DeviceId);
        if (bestEndPoint == null)
            return new SessionResponseEx(SessionErrorCode.GeneralError) { ErrorMessage = "Could not find any free server!" };

        if (!bestEndPoint.Equals(requestEndPoint))
            return new SessionResponseEx(SessionErrorCode.RedirectHost) { RedirectHostEndPoint = bestEndPoint };

        // Add session
        session.Access = null;
        session.Device = null;
        await _vhContext.Sessions.AddAsync(session);
        await _vhContext.SaveChangesAsync();

        session.Access = access;
        session.Device = device;
        await _cacheRepo.AddSession(session);

        _ = TrackSession(device, accessToken.AccessPointGroup!.AccessPointGroupName ?? "farm-" + accessToken.AccessPointGroupId, accessToken.AccessTokenName ?? "token-" + accessToken.AccessTokenId);
        ret.SessionId = (uint)session.SessionId;
        return ret;
    }

    private static bool ValidateServerEndPoint(Models.Server server, IPEndPoint requestEndPoint, Guid accessPointGroupId)
    {
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        // validate request to this server
        var ret = server.AccessPoints!.Any(x =>
            x.TcpPort == requestEndPoint.Port &&
            x.AccessPointGroupId == accessPointGroupId &&
            (x.IpAddress == anyIp.ToString() || x.IpAddress == requestEndPoint.Address.ToString())
        );

        return ret;
    }

    public async Task<SessionResponseEx> GetSession(uint sessionId, string hostEndPoint, string? clientIp, Models.Server server)
    {
        // validate argument
        if (server.AccessPoints == null)
            throw new ArgumentException("AccessPoints is not loaded for this model.", nameof(server));

        _ = clientIp; //we don't not use it now
        var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
        var session = await _cacheRepo.GetSession(sessionId);
        var accessToken = session.Access!.AccessToken!;

        // can server request this endpoint?
        if (!ValidateServerEndPoint(server, requestEndPoint, accessToken.AccessPointGroupId))
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Invalid EndPoint request!"
            };

        // build response
        _vhContext.Attach(session);
        var ret = await BuildSessionResponse(session, DateTime.UtcNow);
        await _vhContext.SaveChangesAsync();
        return ret;
    }

    private async Task<SessionResponseEx> BuildSessionResponse(Session session, DateTime accessTime)
    {
        var access = session.Access!;
        var accessToken = session.Access!.AccessToken!;

        // update session
        access.AccessedTime = accessTime;
        session.AccessedTime = accessTime;

        // create common accessUsage
        var accessUsage = new AccessUsage
        {
            MaxClientCount = accessToken.MaxDevice,
            MaxTraffic = accessToken.MaxTraffic,
            ExpirationTime = access.EndTime,
            SentTraffic = access.TotalSentTraffic - access.CycleSentTraffic,
            ReceivedTraffic = access.TotalReceivedTraffic - access.CycleReceivedTraffic,
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

            var otherSessions = await _cacheRepo.GetActiveSessions(session.AccessId);

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

    public async Task<ResponseBase> AddUsage(uint sessionId, UsageInfo usageInfo, bool closeSession, Models.Server server)
    {
        var session = await _cacheRepo.GetSession(sessionId);
        var access = session.Access ?? throw new Exception($"Could not find access. SessionId: {session.SessionId}");
        var accessToken = session.Access?.AccessToken ?? throw new Exception("AccessToken is not loaded by cache.");
        var accessedTime = DateTime.UtcNow;

        // check projectId
        if (accessToken.ProjectId != server.ProjectId)
            throw new AuthenticationException();

        // update access if session is open
        if (session.EndTime == null)
        {
            _logger.LogInformation(
                $"AddUsage to {access.AccessId}, SentTraffic: {usageInfo.SentTraffic / 1000000} MB, ReceivedTraffic: {usageInfo.ReceivedTraffic / 1000000} MB");

            // add usage to access
            access.TotalReceivedTraffic += usageInfo.ReceivedTraffic;
            access.TotalSentTraffic += usageInfo.SentTraffic;
            access.AccessedTime = accessedTime;

            // insert AccessUsageLog
            if (usageInfo.ReceivedTraffic != 0 || usageInfo.SentTraffic != 0)
                _cacheRepo.AddAccessUsage(new AccessUsageEx
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

            _ = TrackUsage(server, accessToken, accessToken.AccessPointGroup!.AccessPointGroupName, session.Device!,
                usageInfo);
        }
        else if (usageInfo.ReceivedTraffic != 0 || usageInfo.SentTraffic != 0)
        {
            _logger.LogWarning("Can not add usage to a closed session. SessionId: {sessionId}, Traffic: {traffic}",
                sessionId, usageInfo.ReceivedTraffic + usageInfo.SentTraffic);
        }

        // build response
        var ret = await BuildSessionResponse(session, accessedTime);

        // close session
        if (closeSession)
        {
            _logger.LogWarning("Close Session Requested. SessionId: {SessionId}", sessionId); //todo
            if (ret.ErrorCode == SessionErrorCode.Ok)
                session.ErrorCode = SessionErrorCode.SessionClosed;
            session.EndTime ??= session.EndTime = DateTime.UtcNow;
        }

        return new ResponseBase(ret);
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(Models.Server currentServer, IPEndPoint currentEndPoint, Guid accessPointGroupId, Guid deviceId)
    {
        // prevent re-redirect if device has already redirected to this server
        var cacheKey = $"LastDeviceServer/{deviceId}";
        if (!_agentOptions.AllowRedirect ||
            (_memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) && lastDeviceServerId == currentServer.ServerId))
        {
            if (IsServerReady(currentServer))
                return currentEndPoint;
        }

        // get all servers of this farm
        var servers = (await _cacheRepo.GetServers()).Values.ToArray();
        servers = servers.Where(x => x?.ProjectId == currentServer.ProjectId && IsServerReady(x)).ToArray();

        // find all accessPoints belong to this farm
        var accessPoints = new List<AccessPoint>();
        foreach (var server in servers)
            foreach (var accessPoint in server!.AccessPoints!.Where(x =>
                         x.AccessPointGroupId == accessPointGroupId &&
                         x.AccessPointMode is AccessPointMode.PublicInToken or AccessPointMode.Public &&
                         IPAddress.Parse(x.IpAddress).AddressFamily == currentEndPoint.AddressFamily))
            {
                accessPoint.Server = server;
                accessPoints.Add(accessPoint);
            }

        // find the best free server
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

    private bool IsServerReady(Models.Server currentServer)
    {
        return ServerUtil.IsServerReady(currentServer, _agentOptions.LostServerThreshold);
    }
}