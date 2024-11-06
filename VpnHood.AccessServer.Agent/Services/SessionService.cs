using System.Net;
using System.Security.Authentication;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Agent.Utils;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Manager.Common.Utils;
using VpnHood.Server.Access.Messaging;
using AsyncLock = GrayMint.Common.Utils.AsyncLock;

namespace VpnHood.AccessServer.Agent.Services;

public class SessionService(
    ILogger<SessionService> logger,
    ILogger<SessionService.NewAccess> newAccessLogger,
    IOptions<AgentOptions> agentOptions,
    CacheService cacheService,
    VhAgentRepo vhAgentRepo,
    [FromKeyedServices(Program.LocationProviderDevice)]
    IIpLocationProvider deviceLocationProvider,
    ServerSelectorService serverSelectorService)
{
    public class NewAccess;

    private static Task TrackUsage(ProjectCache project, ServerCache server,
        SessionCache session, AccessCache access, Traffic traffic, bool adReward)
    {
        if (string.IsNullOrEmpty(project.GaMeasurementId) || string.IsNullOrEmpty(project.GaApiSecret))
            return Task.CompletedTask;

        var ga4Tracker = new Ga4TagTracker {
            MeasurementId = project.GaMeasurementId,
            // ApiSecret = project.GaApiSecret, //not used yet
            UserAgent = session.UserAgent ?? "",
            ClientId = session.ClientId.ToString(),
            SessionId = session.ServerId.ToString(),
            UserId = access.AccessTokenId.ToString(),
            SessionCount = 1
        };

        var ga4Event = new Ga4TagEvent {
            EventName = TrackEventNames.PageView,
            DocumentLocation = $"{server.ServerFarmName}/{server.ServerName}",
            DocumentTitle = $"{server.ServerFarmName}",
            Properties = new Dictionary<string, object> {
                { "DeviceId", session.DeviceId },
                { "TokenId", access.AccessTokenId },
                { "TokenCode", access.AccessTokenSupportCode },
                { "ServerId", server.ServerId },
                { "FarmId", server.ServerFarmId },
                { "Traffic", Math.Round(traffic.Total / 1_000_000d) },
                { "Sent", Math.Round(traffic.Sent / 1_000_000d) },
                { "Received", Math.Round(traffic.Received / 1_000_000d) },
                { "adReward", adReward }
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
        try {
            var sessionResponseEx = await CreateSessionInternal(server, sessionRequestEx);
            return sessionResponseEx;
        }
        catch (SessionExceptionEx ex) {
            return ex.SessionResponseEx;
        }
        finally {
            // make sure to save changes if any error occured or return session exception
            // such as creating new device or access
            await vhAgentRepo.SaveChangesAsync();
        }
    }

    private async Task<SessionResponseEx> CreateSessionInternal(ServerCache serverCache, SessionRequestEx sessionRequestEx)
    {
        // validate argument
        if (serverCache.AccessPoints == null)
            throw new ArgumentException("AccessPoints is not loaded for this model.", nameof(serverCache));

        // extract required data
        var projectId = serverCache.ProjectId;
        var serverId = serverCache.ServerId;
        var clientIp = sessionRequestEx.ClientIp;
        var clientInfo = sessionRequestEx.ClientInfo;
        var projectCache = await cacheService.GetProject(serverCache.ProjectId);

        // Get accessTokenModel
        var accessToken = await vhAgentRepo.AccessTokenGet(projectId, Guid.Parse(sessionRequestEx.TokenId));
        var serverFarmCache = await cacheService.GetServerFarm(serverCache.ServerFarmId);

        // validate the request
        if (!ValidateTokenRequest(sessionRequestEx, accessToken.Secret))
            throw new SessionExceptionEx(SessionErrorCode.AccessError, "Could not validate the request.");

        // can serverModel request this endpoint?
        if (accessToken.ServerFarmId != serverCache.ServerFarmId)
            throw new SessionExceptionEx(SessionErrorCode.AccessError, "Token does not belong to server farm.");

        // check is token locked
        if (!accessToken.IsEnabled)
            throw new SessionExceptionEx(SessionErrorCode.AccessLocked,
                accessToken.Description?.Contains("#message") == true
                    ? accessToken.Description.Replace("#message", "").Trim()
                    : "Your access has been locked! Please contact the support.");

        // set accessTokenModel expiration time on first use
        if (accessToken.Lifetime != 0 && accessToken.FirstUsedTime != null &&
            accessToken.FirstUsedTime + TimeSpan.FromDays(accessToken.Lifetime) < DateTime.UtcNow)
            throw new SessionExceptionEx(SessionErrorCode.AccessExpired,
                "Your access has been expired! Please contact the support.");

        if (accessToken.ExpirationTime < DateTime.UtcNow)
            throw new SessionExceptionEx(SessionErrorCode.AccessExpired,
                "Your access has been expired! Please contact the support.");

        // check is Ip Locked
        var ipLock = clientIp != null ? await vhAgentRepo.IpLockFind(projectId, clientIp.ToString()) : null;
        if (ipLock?.LockedTime != null)
            throw new SessionExceptionEx(SessionErrorCode.AccessLocked,
                "Your access has been locked! Please contact the support.");

        // block bad ad requests clients
        if (accessToken.AccessTokenId == Guid.Parse("77d58603-cdcb-4efc-992f-c132be1de0e3") &&
            !string.IsNullOrEmpty(clientInfo.ClientVersion)) {
            if (Version.TryParse(clientInfo.ClientVersion, out var clVersion) && clVersion.Build < 512)
                throw new SessionExceptionEx(SessionErrorCode.AccessLocked,
                    "Please update to the latest VpnHood! CONNECT app. The version should be 512 or later.");
        }

        // create client or update if changed
        var clientIpToStore = clientIp != null ? IPAddressUtil.Anonymize(clientIp).ToString() : null;
        var device = await vhAgentRepo.DeviceFind(projectId, clientInfo.ClientId);
        if (device == null) {
            device = new DeviceModel {
                DeviceId = Guid.NewGuid(),
                ProjectId = projectId,
                ClientId = Guid.Parse(clientInfo.ClientId),
                IpAddress = clientIpToStore,
                ClientVersion = clientInfo.ClientVersion,
                UserAgent = clientInfo.UserAgent,
                CreatedTime = DateTime.UtcNow,
                LastUsedTime = DateTime.UtcNow,
                Country = await GetCountryCode(clientIp),
            };
            device = await vhAgentRepo.DeviceAdd(device);
        }
        else {
            if (string.IsNullOrEmpty(device.Country) || device.IpAddress != clientIpToStore)
                device.Country = await GetCountryCode(clientIp, device.Country);

            device.UserAgent = clientInfo.UserAgent;
            device.ClientVersion = clientInfo.ClientVersion;
            device.LastUsedTime = DateTime.UtcNow;
            device.IpAddress = clientIpToStore; //must after set the country
        }

        // check has device Locked
        if (device.LockedTime != null)
            throw new SessionExceptionEx(SessionErrorCode.AccessLocked,
                "Your access has been locked! Please contact the support.");

        // get access
        Guid? accessDeviceId = accessToken.IsPublic ? device.DeviceId : null ;
        var accessCache = await cacheService.GetAccessByTokenId(accessToken, accessDeviceId);
        accessCache.LastUsedTime = DateTime.UtcNow; // update used time
        
        // check supported version
        if (string.IsNullOrEmpty(clientInfo.ClientVersion) ||
            Version.Parse(clientInfo.ClientVersion).CompareTo(AgentOptions.MinClientVersion) < 0)
            throw new SessionExceptionEx(SessionErrorCode.UnsupportedClient,
                "This version is not supported! You need to update your app.");

        // Calc server select options
        var policyResult = ClientPolicyCalculator.Calculate(projectCache, serverFarmCache, accessToken, accessCache,
            sessionRequestEx, clientCountry: device.Country, allowRedirect: agentOptions.Value.AllowRedirect);

        // Check Redirect to another server if everything was ok
        await serverSelectorService.CheckRedirect(serverCache, policyResult.ServerSelectOptions);

        // check is device already rewarded
        var isAdRewardedDevice = cacheService.Ad_IsRewardedAccess(accessCache.AccessId) &&
                                 accessToken.Description?.Contains("#ad-debugger") is null or false; //for ad debuggers

        //todo
        var isAdRequired = accessToken.AdRequirement is AdRequirement.Required && !isAdRewardedDevice;

        // create session
        var sessionCache = new SessionCache {
            SessionId = 0,
            ProjectId = device.ProjectId,
            AccessId = accessCache.AccessId,
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
            IsArchived = false,
            Country = device.Country,
            UserAgent = device.UserAgent,
            ClientId = device.ClientId,
            IsAdRewardPending = policyResult.AdRequirement == AdRequirement.Required,
            IsAdReward = policyResult.IsPremiumByAdReward,
            IsTrial = policyResult.IsPremiumByTrial,
            IsPremium = policyResult.IsPremiumToken,
            ExpirationTime = policyResult.ExpirationTime
        };

        var ret = await BuildSessionResponse(sessionCache, accessCache);
        ret.AdRequirement = policyResult.AdRequirement;
        ret.GaMeasurementId = projectCache.GaMeasurementId;
        ret.ServerLocation = serverCache.LocationInfo.ServerLocation;
        ret.ServerTags = serverCache.Tags;
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // update AccessToken
        if (serverFarmCache.TokenJson == null) throw new Exception("TokenJson is not initialized for this farm.");
        accessToken.FirstUsedTime ??= sessionCache.CreatedTime;
        accessToken.LastUsedTime = sessionCache.CreatedTime;
        accessCache.AdRewardMinutes = policyResult.AdRewardMinutes;


        // push token to client if add server with PublicInToken are ready
        var pushTokenToClient = 
            serverFarmCache.PushTokenToClient && await serverSelectorService.IsAllPublicInTokenServersReady(serverFarmCache.ServerFarmId);

        var farmToken = pushTokenToClient ? FarmTokenBuilder.GetServerToken(serverFarmCache.TokenJson) : null;
        if (farmToken != null)
            ret.AccessKey = accessToken.ToToken(farmToken).ToAccessKey();

        // Add session to database
        var sessionModel = await vhAgentRepo.SessionAdd(sessionCache);
        await vhAgentRepo.SaveChangesAsync();
        sessionCache.SessionId = sessionModel.SessionId;

        // Add session to cache
        await cacheService.AddSession(sessionCache);
        logger.LogInformation("New Session has been created. SessionId: {SessionId}", sessionCache.SessionId);

        ret.SessionId = (ulong)sessionCache.SessionId;
        return ret;
    }

    private async Task<string?> GetCountryCode(IPAddress? clientIp, string? defaultValue = null)
    {
        if (clientIp == null)
            return defaultValue;

        try {
            var location = await deviceLocationProvider.GetLocation(clientIp, CancellationToken.None);
            return location.CountryCode;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "Could not get location for ClientIp: {ClientIp}", clientIp);
            return defaultValue;
        }
    }

    public async Task<SessionResponseEx> GetSession(ServerCache server, uint sessionId, string hostEndPoint,
        string? clientIp)
    {
        _ = clientIp; //we don't use it now
        _ = hostEndPoint; //we don't use it now

        try {
            var session = await cacheService.GetSession(server.ServerId, sessionId);
            var access = await cacheService.GetAccess(session.AccessId);
            access.LastUsedTime = DateTime.UtcNow;

            // build response
            var ret = await BuildSessionResponse(session, access);
            logger.LogInformation("Reporting a session. SessionId: {SessionId}, EndTime: {EndTime}", sessionId,
                session.EndTime);
            return ret;
        }
        catch (KeyNotFoundException) {
            logger.LogWarning(
                "Server requested a session that does not exists SessionId: {SessionId}, ServerId: {ServerId}",
                sessionId, server.ServerId);
            throw;
        }
    }

    private static void UpdateSession(SessionBaseModel session, AccessCache access, SessionCache[] otherSessions, TimeSpan adRewardPendingTimeout)
    {
        var utcNow = DateTime.UtcNow;
        session.LastUsedTime = access.LastUsedTime;

        // session is already closed
        if (session.ErrorCode != SessionErrorCode.Ok)
            return;

        // make sure access is enabled
        if (!access.IsAccessTokenEnabled) {
            session.Close(SessionErrorCode.AccessLocked, "Your access has been locked! Please contact the support.");
            return;
        }

        // update expiration time if it is not trial or ad reward
        if (session is { IsTrial: false, IsAdReward: false })
            session.ExpirationTime = access.ExpirationTime;

        // check token expiration
        if (access.ExpirationTime != null && access.ExpirationTime < utcNow) {
            session.Close(SessionErrorCode.AccessExpired, "Access has been expired.");
            return;
        }

        // check token expiration
        if (session is { IsAdReward: true, IsAdRewardPending: false } && access.AdRewardExpirationTime < utcNow) {
            session.Close(SessionErrorCode.AccessExpired, "Reward has been consumed.");
            return;
        }

        // check ad expiration
        if (session is { IsAdReward: true, IsAdRewardPending: true } && (utcNow - session.CreatedTime) > adRewardPendingTimeout) {
            session.Close(SessionErrorCode.AdError,
                "The reward for watching an ad has not been granted within the expected timeframe.");
            return;
        }

        // check traffic
        if (access.MaxTraffic != 0 && access.TotalSentTraffic + access.CycleReceivedTraffic > access.MaxTraffic) {
            session.Close(SessionErrorCode.AccessTrafficOverflow, "All traffic quota has been consumed.");
            return;
        }

        // suppressedTo yourself
        var selfSessions = otherSessions.Where(x =>
            x.DeviceId == session.DeviceId && x.SessionId != session.SessionId).ToArray();
        if (selfSessions.Any()) {
            session.SuppressedTo = SessionSuppressType.YourSelf;
            foreach (var selfSession in selfSessions) {
                selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                selfSession.Close(SessionErrorCode.SessionSuppressedBy, "Session has been suppressed by yourself.");
            }
        }

        // suppressedTo others by MaxClientCount
        if (access.MaxDevice != 0) {
            var otherSessions2 = otherSessions
                .Where(x => x.DeviceId != session.DeviceId && x.SessionId != session.SessionId)
                .OrderBy(x => x.CreatedTime).ToArray();
            for (var i = 0; i <= otherSessions2.Length - access.MaxDevice; i++) {
                var otherSession = otherSessions2[i];
                otherSession.SuppressedBy = SessionSuppressType.Other;
                otherSession.Close(SessionErrorCode.SessionSuppressedBy, "Session has been suppressed by other.");
                session.SuppressedTo = SessionSuppressType.Other;
            }
        }
    }

    private async Task<SessionResponseEx> BuildSessionResponse(SessionBaseModel session, AccessCache access)
    {
        var activeSession = !access.IsPublic ? await cacheService.GetActiveSessions(session.AccessId) : [];

        // update session
        UpdateSession(session, access, activeSession, adRewardPendingTimeout: agentOptions.Value.AdRewardPendingTimeout);

        // create common accessUsage
        var accessUsage = new AccessUsage {
            Traffic = new Traffic {
                Sent = access.TotalSentTraffic - access.LastCycleSentTraffic,
                Received = access.TotalReceivedTraffic - access.LastCycleReceivedTraffic
            },
            MaxClientCount = access.MaxDevice,
            MaxTraffic = access.MaxTraffic,
            ExpirationTime = session.ExpirationTime,
            ActiveClientCount = activeSession.Count(x => x.EndTime == null)
        };

        // build result
        return new SessionResponseEx {
            ErrorCode = session.ErrorCode,
            ExtraData = session.ExtraData,
            SessionId = (uint)session.SessionId,
            CreatedTime = session.CreatedTime,
            SessionKey = session.SessionKey,
            SuppressedTo = session.SuppressedTo,
            SuppressedBy = session.SuppressedBy,
            ErrorMessage = session.ErrorMessage,
            AccessUsage = accessUsage,
            ExpirationTime = session.ExpirationTime,
            RedirectHostEndPoint = null,
            RedirectHostEndPoints = null
        };
    }

    public async Task<SessionResponse> AddUsage(ServerCache server, uint sessionId, Traffic traffic,
        bool closeSession, string? adData)
    {
        var sessionCache = await cacheService.GetSession(server.ServerId, sessionId);
        var accessCache = await cacheService.GetAccess(sessionCache.AccessId);
        var projectCache = await cacheService.GetProject(sessionCache.ProjectId);
        var utcTime = DateTime.UtcNow;

        // check projectId
        if (sessionCache.ProjectId != server.ProjectId)
            throw new AuthenticationException();

        // give ad reward
        var isAdReward = !string.IsNullOrEmpty(adData);
        if (!string.IsNullOrEmpty(adData)) {
            if (!await VerifyAdReward(sessionCache.ProjectId, accessCache.AccessId, adData)) {
                sessionCache.Close(SessionErrorCode.AdError, "Could not verify the given rewarded ad.");
                return await BuildSessionResponse(sessionCache, accessCache);
            }

            switch (accessCache.AdRewardMinutes) {
                case null:
                    sessionCache.Close(SessionErrorCode.AdError, "Access does not support rewarded ad.");
                    return await BuildSessionResponse(sessionCache, accessCache);

                case 0:
                    accessCache.AdRewardExpirationTime = null;
                    sessionCache.ExpirationTime = null;
                    break;

                default:
                    // increase current reward if it is not expired
                    accessCache.AdRewardExpirationTime = accessCache.AdRewardExpirationTime > utcTime
                        ? accessCache.AdRewardExpirationTime.Value.AddMinutes(accessCache.AdRewardMinutes.Value)
                        : utcTime.AddMinutes(accessCache.AdRewardMinutes.Value);
                    break;
            }

            sessionCache.ExpirationTime = accessCache.AdRewardExpirationTime;
            sessionCache.IsAdReward = true;
            sessionCache.IsAdRewardPending = false;
        }

        // update access if session is open
        logger.LogInformation(
            "AddUsage to a session. SessionId: {SessionId}, " +
            "SentTraffic: {SendTraffic} Bytes, ReceivedTraffic: {ReceivedTraffic} Bytes, Total: {Total}, " +
            "EndTime: {EndTime}. AdReward: {AdReward}",
            sessionId, traffic.Sent, traffic.Received, VhUtil.FormatBytes(traffic.Total), sessionCache.EndTime, isAdReward);

        // add usage to access
        accessCache.TotalReceivedTraffic += traffic.Received;
        accessCache.TotalSentTraffic += traffic.Sent;
        accessCache.LastUsedTime = DateTime.UtcNow;

        // insert AccessUsageLog
        cacheService.AddSessionUsage(new AccessUsageModel {
            AccessTokenId = accessCache.AccessTokenId,
            ServerFarmId = server.ServerFarmId,
            ReceivedTraffic = traffic.Received,
            SentTraffic = traffic.Sent,
            TotalReceivedTraffic = accessCache.TotalReceivedTraffic,
            TotalSentTraffic = accessCache.TotalSentTraffic,
            DeviceId = sessionCache.DeviceId,
            LastCycleReceivedTraffic = accessCache.LastCycleReceivedTraffic,
            LastCycleSentTraffic = accessCache.LastCycleSentTraffic,
            CreatedTime = DateTime.UtcNow,
            AccessId = sessionCache.AccessId,
            SessionId = sessionCache.SessionId,
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            IsAdReward = sessionCache.IsAdReward
        });

        // track
        _ = TrackUsage(projectCache, server, sessionCache, accessCache, traffic, isAdReward);

        // build response
        var sessionResponse = await BuildSessionResponse(sessionCache, accessCache);

        // close session
        if (closeSession) {
            logger.LogInformation(
                "Session has been closed by user. SessionId: {SessionId}, ResponseCode: {ResponseCode}",
                sessionId, sessionResponse.ErrorCode);

            sessionCache.Close(SessionErrorCode.SessionClosed, "Session closed by client request.");
        }

        return sessionResponse;
    }

    private async Task<bool> VerifyAdReward(Guid projectId, Guid accessId, string adData)
    {
        // check is device rewarded
        for (var i = 0; i < 5; i++) {
            if (cacheService.Ad_RemoveRewardData(projectId, adData)) {
                cacheService.Ad_AddRewardedAccess(accessId);
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }
}