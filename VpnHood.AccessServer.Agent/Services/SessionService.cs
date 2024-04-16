using System.Net;
using System.Security.Authentication;
using Ga4.Ga4Tracking;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Messaging;
using AsyncLock = GrayMint.Common.Utils.AsyncLock;

namespace VpnHood.AccessServer.Agent.Services;

public class SessionService(
    ILogger<SessionService> logger,
    ILogger<SessionService.NewAccess> newAccessLogger,
    IOptions<AgentOptions> agentOptions,
    IMemoryCache memoryCache,
    CacheService cacheService,
    VhRepo vhRepo,
    VhAgentRepo vhAgentRepo)
{
    public class NewAccess;
    private static Task TrackUsage(ProjectCache project, ServerCache server,
        SessionCache session, AccessCache access, Traffic traffic, bool adReward)
    {
        if (string.IsNullOrEmpty(project.GaMeasurementId) || string.IsNullOrEmpty(project.GaApiSecret))
            return Task.CompletedTask;

        var ga4Tracker = new Ga4Tracker
        {
            MeasurementId = project.GaMeasurementId,
            ApiSecret = project.GaApiSecret,
            UserAgent = session.UserAgent ?? "",
            ClientId = session.ClientId.ToString(),
            SessionId = session.ServerId.ToString(),
            UserId = access.AccessTokenId.ToString(),
            SessionCount = 1
        };

        var ga4Event = new Ga4TagEvent
        {
            EventName = Ga4TagEvents.PageView,
            DocumentLocation = $"{server.ServerFarmName}/{server.ServerName}",
            DocumentTitle = $"{server.ServerFarmName}",
            Properties = new Dictionary<string, object>
            {
                {"DeviceId", session.DeviceId},
                {"TokenId", access.AccessTokenId},
                {"TokenCode", access.AccessTokenSupportCode},
                {"ServerId", server.ServerId},
                {"FarmId", server.ServerFarmId},
                {"Traffic", Math.Round(traffic.Total / 1_000_000d)},
                {"Sent", Math.Round(traffic.Sent / 1_000_000d)},
                {"Received", Math.Round(traffic.Received / 1_000_000d)},
                {"adReward", adReward}
        }
        };

        if (!string.IsNullOrEmpty(access.AccessTokenName)) ga4Event.Properties.Add("TokenName", access.AccessTokenName);
        if (!string.IsNullOrEmpty(server.ServerName)) ga4Event.Properties.Add("ServerName", server.ServerName);
        if (!string.IsNullOrEmpty(server.ServerFarmName)) ga4Event.Properties.Add("FarmName", server.ServerFarmName);

        return ga4Tracker.Track(ga4Event);
    }

    private static bool ValidateTokenRequest(SessionRequestEx sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = VhUtil.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public async Task<SessionResponseEx> CreateSession(ServerCache server, SessionRequestEx sessionRequestEx)
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
        var project = await cacheService.GetProject(server.ProjectId);

        // Get accessTokenModel
        var accessToken = await vhRepo.AccessTokenGet(projectId, Guid.Parse(sessionRequestEx.TokenId), true);

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
        var ipLock = clientIp != null ? await vhAgentRepo.IpLockFind(projectId, clientIp.ToString()) : null;
        if (ipLock?.LockedTime != null)
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };

        // create client or update if changed
        var clientIpToStore = clientIp != null ? IPAddressUtil.Anonymize(clientIp).ToString() : null;
        var device = await vhAgentRepo.DeviceFind(projectId, clientInfo.ClientId);
        if (device == null)
        {
            device = new DeviceModel
            {
                DeviceId = Guid.NewGuid(),
                ProjectId = projectId,
                ClientId = clientInfo.ClientId,
                IpAddress = clientIpToStore,
                ClientVersion = clientInfo.ClientVersion,
                UserAgent = clientInfo.UserAgent,
                CreatedTime = DateTime.UtcNow,
                ModifiedTime = DateTime.UtcNow
            };
            device = await vhRepo.AddAsync(device);
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
        using var accessLock = await AsyncLock.LockAsync($"CreateSession_AccessId_{accessToken.AccessTokenId}_{deviceId}");
        var access = await cacheService.GetAccessByTokenId(accessToken.AccessTokenId, deviceId);
        if (access == null)
        {
            access = await vhAgentRepo.AddNewAccess(accessToken.AccessTokenId, deviceId);
            newAccessLogger.LogInformation(
                "New Access has been created. AccessId: {access.AccessId}, ProjectName: {ProjectName}, FarmName: {FarmName}",
                access.AccessId, project.ProjectName, accessToken.ServerFarm?.ServerFarmName);
        }
        else
        {
            accessLock.Dispose(); // access is already exists so the next call will not create new
        }
        access.LastUsedTime = DateTime.UtcNow; // update used time

        // check supported version
        if (string.IsNullOrEmpty(clientInfo.ClientVersion) || Version.Parse(clientInfo.ClientVersion).CompareTo(AgentOptions.MinClientVersion) < 0)
            return new SessionResponseEx(SessionErrorCode.UnsupportedClient) { ErrorMessage = "This version is not supported! You need to update your app." };

        // Check Redirect to another server if everything was ok
        var bestTcpEndPoint = await FindBestServerForDevice(server, requestEndPoint, accessToken.ServerFarmId, device.DeviceId);
        if (bestTcpEndPoint == null)
            return new SessionResponseEx(SessionErrorCode.AccessError) { ErrorMessage = "Could not find any free server!" };

        // redirect if current server does not serve the best TcpEndPoint
        if (!server.AccessPoints.Any(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort).Equals(bestTcpEndPoint)))
            return new SessionResponseEx(SessionErrorCode.RedirectHost) { RedirectHostEndPoint = bestTcpEndPoint };

        // validate ad
        if (!string.IsNullOrEmpty(sessionRequestEx.AdData) && !cacheService.RemoveAd(projectId, sessionRequestEx.AdData))
            return new SessionResponseEx(SessionErrorCode.AdError)
            {
                ErrorMessage = "Invalid Ad. Please contact support."
            };

        // create session
        var session = new SessionCache
        {
            SessionId = 0,
            ProjectId = device.ProjectId,
            AccessId = access.AccessId,
            DeviceId = device.DeviceId,
            SessionKey = VhUtil.GenerateKey(),
            CreatedTime = DateTime.UtcNow,
            LastUsedTime = DateTime.UtcNow,
            DeviceIp = clientIpToStore,
            ClientVersion = clientInfo.ClientVersion,
            EndTime = null,
            ServerId = serverId,
            ExtraData = sessionRequestEx.ExtraData,
            SuppressedBy = SessionSuppressType.None,
            SuppressedTo = SessionSuppressType.None,
            ErrorCode = SessionErrorCode.Ok,
            ErrorMessage = null,
            Country = null,
            IsArchived = false,
            UserAgent = device.UserAgent,
            ClientId = device.ClientId,
            IsAdReward = !string.IsNullOrEmpty(sessionRequestEx.AdData),
            AdExpirationTime = accessToken.IsAdRequired && string.IsNullOrEmpty(sessionRequestEx.AdData)
                ? DateTime.UtcNow + agentOptions.Value.AdTimeout : null
        };

        var ret = await BuildSessionResponse(session, access);
        ret.ExtraData = session.ExtraData;
        ret.GaMeasurementId = project.GaMeasurementId;
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // update AccessToken
        if (accessToken.ServerFarm?.TokenJson == null) throw new Exception("TokenJson is not initialized for this farm.");
        accessToken.FirstUsedTime ??= session.CreatedTime;
        accessToken.LastUsedTime = session.CreatedTime;

        // push token to client
        var farmToken = accessToken.ServerFarm.PushTokenToClient ? FarmTokenBuilder.GetUsableToken(accessToken.ServerFarm) : null;
        if (farmToken != null)
            ret.AccessKey = new Token
            {
                ServerToken = farmToken,
                Secret = accessToken.Secret,
                TokenId = accessToken.AccessTokenId.ToString(),
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode.ToString()
            }.ToAccessKey();

        // Add session to database
        var sessionModel = await vhAgentRepo.AddSession(session);
        await vhAgentRepo.SaveChangesAsync();
        session.SessionId = sessionModel.SessionId;

        // Add session to cache
        await cacheService.AddSession(session);
        logger.LogInformation("New Session has been created. SessionId: {SessionId}", session.SessionId);

        ret.SessionId = (ulong)session.SessionId;
        return ret;
    }

    public async Task<SessionResponseEx> GetSession(ServerCache server, uint sessionId, string hostEndPoint, string? clientIp)
    {
        _ = clientIp; //we don't use it now
        _ = hostEndPoint; //we don't use it now

        try
        {
            var session = await cacheService.GetSession(server.ServerId, sessionId);
            var access = await cacheService.GetAccess(session.AccessId);
            access.LastUsedTime = DateTime.UtcNow;

            // build response
            var ret = await BuildSessionResponse(session, access);
            ret.ExtraData = session.ExtraData;
            logger.LogInformation("Reporting a session. SessionId: {SessionId}, EndTime: {EndTime}", sessionId, session.EndTime);
            return ret;
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning("Server requested a session that does not exists SessionId: {SessionId}, ServerId: {ServerId}",
                sessionId, server.ServerId);
            throw;
        }
    }

    private async Task<SessionResponseEx> BuildSessionResponse(
        SessionBaseModel session, AccessCache access)
    {
        // update session
        session.LastUsedTime = access.LastUsedTime;

        // create common accessUsage
        var accessUsage = new AccessUsage
        {
            Traffic = new Traffic
            {
                Sent = access.TotalSentTraffic - access.LastCycleSentTraffic,
                Received = access.TotalReceivedTraffic - access.LastCycleReceivedTraffic
            },
            MaxClientCount = access.MaxDevice,
            MaxTraffic = access.MaxTraffic,
            ExpirationTime = access.ExpirationTime,
            ActiveClientCount = 0
        };

        // validate session status
        if (session.ErrorCode == SessionErrorCode.Ok)
        {
            // check token expiration
            if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx(SessionErrorCode.AccessExpired)
                { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

            // check token expiration
            if (session.AdExpirationTime != null && session.AdExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx(SessionErrorCode.AdError)
                { AccessUsage = accessUsage, ErrorMessage = "The reward for watching an ad has not been granted within the expected timeframe." };

            // check traffic
            if (accessUsage.MaxTraffic != 0 &&
                accessUsage.Traffic.Total > accessUsage.MaxTraffic)
                return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

            var otherSessions = await cacheService.GetActiveSessions(session.AccessId);

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

            accessUsage.ActiveClientCount = access.IsPublic ? 0 : otherSessions.Count(x => x.EndTime == null);
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

    public async Task<SessionResponse> AddUsage(ServerCache server, uint sessionId, Traffic traffic,
        bool closeSession, string? adData)
    {
        var session = await cacheService.GetSession(server.ServerId, sessionId);
        var access = await cacheService.GetAccess(session.AccessId);
        var project = await cacheService.GetProject(session.ProjectId);

        // check projectId
        if (session.ProjectId != server.ProjectId)
            throw new AuthenticationException();

        // validate ad
        var adReward = session.AdExpirationTime != null && !string.IsNullOrEmpty(adData);
        if (!string.IsNullOrEmpty(adData) && !cacheService.RemoveAd(session.ProjectId, adData))
            return new SessionResponseEx(SessionErrorCode.AdError)
            {
                ErrorMessage = "Invalid Ad. Please contact support."
            };

        // update access if session is open
        logger.LogInformation(
            "AddUsage to a session. SessionId: {SessionId}, " +
            "SentTraffic: {SendTraffic} Bytes, ReceivedTraffic: {ReceivedTraffic} Bytes, Total: {Total}, " +
            "EndTime: {EndTime}. AdReward: {AdReward}",
            sessionId, traffic.Sent, traffic.Received, VhUtil.FormatBytes(traffic.Total), session.EndTime, adReward);

        // add usage to access
        access.TotalReceivedTraffic += traffic.Received;
        access.TotalSentTraffic += traffic.Sent;
        access.LastUsedTime = DateTime.UtcNow;

        // insert AccessUsageLog
        cacheService.AddSessionUsage(new AccessUsageModel
        {
            AccessTokenId = access.AccessTokenId,
            ServerFarmId = server.ServerFarmId,
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
            IsAdReward = adReward
        });

        // give ad reward
        if (adReward)
            session.AdExpirationTime = null;

        // track
        _ = TrackUsage(project, server, session, access, traffic, adReward);

        // build response
        var sessionResponse = await BuildSessionResponse(session, access);

        // close session
        if (closeSession)
        {
            logger.LogInformation(
                "Session has been closed by user. SessionId: {SessionId}, ResponseCode: {ResponseCode}",
                sessionId, sessionResponse.ErrorCode);

            if (sessionResponse.ErrorCode == SessionErrorCode.Ok)
                session.ErrorCode = SessionErrorCode.SessionClosed;

            session.EndTime ??= DateTime.UtcNow;
        }

        return sessionResponse;
    }

    public async Task<IPEndPoint?> FindBestServerForDevice(ServerCache currentServer, IPEndPoint currentEndPoint, Guid serverFarmId, Guid deviceId)
    {
        // prevent re-redirect if device has already redirected to this serverModel
        var cacheKey = $"LastDeviceServer/{serverFarmId}/{deviceId}";
        if (!agentOptions.Value.AllowRedirect ||
            (memoryCache.TryGetValue(cacheKey, out Guid lastDeviceServerId) && lastDeviceServerId == currentServer.ServerId))
        {
            if (currentServer.IsReady)
                return currentEndPoint;
        }

        // get all servers of this farm
        var servers = await cacheService.GetServers();
        var farmServers = servers
            .Where(server =>
                server.ProjectId == currentServer.ProjectId &&
                server.ServerFarmId == currentServer.ServerFarmId &&
                server.AccessPoints.Any(accessPoint => accessPoint.IsPublic && accessPoint.IpAddress.AddressFamily == currentEndPoint.AddressFamily) &&
                server.IsReady)
            .ToArray();

        // find the best free server
        var bestServer = farmServers
            .MinBy(server => server.ServerStatus!.SessionCount);

        if (bestServer != null)
        {
            memoryCache.Set(cacheKey, bestServer.ServerId, TimeSpan.FromMinutes(5));
            var serverEndPoint = bestServer.AccessPoints.First(accessPoint =>
                accessPoint.IsPublic && accessPoint.IpAddress.AddressFamily == currentEndPoint.AddressFamily);
            var ret = new IPEndPoint(serverEndPoint.IpAddress, serverEndPoint.TcpPort);
            return ret;
        }

        return null;
    }
}
