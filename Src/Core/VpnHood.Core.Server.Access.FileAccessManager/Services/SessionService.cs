﻿using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement.Dtos;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement.Services;

public class SessionService : IDisposable
{
    public bool IsUnitTest { get; }
    private const string SessionFileExtension = "session";
    private readonly TimeSpan _sessionPermanentlyTimeout = TimeSpan.FromHours(48);
    private readonly TimeSpan _sessionTemporaryTimeout = TimeSpan.FromHours(20);
    private readonly TimeSpan _adRequiredTimeout = TimeSpan.FromMinutes(4);
    private readonly TimeSpan _trialTimeout = TimeSpan.FromMinutes(10);
    private long _lastSessionId;
    private readonly string _sessionsFolderPath;
    private readonly ConcurrentDictionary<ulong, bool> _updatedSessionIds = new();
    private readonly Job _cleanupSessionsJob;
    public ConcurrentDictionary<ulong, Session> Sessions { get; }


    public SessionService(string sessionsFolderPath, bool isUnitTest)
    {
        IsUnitTest = isUnitTest;
        _sessionsFolderPath = sessionsFolderPath;
        Directory.CreateDirectory(sessionsFolderPath);

        // load all previous sessions to dictionary
        Sessions = LoadAllSessions(sessionsFolderPath);
        if (Sessions.Any())
            _lastSessionId = Sessions.Max(x => (long)x.Key);

        _cleanupSessionsJob = new Job(Cleanup, name: nameof(SessionService));
    }

    private static ConcurrentDictionary<ulong, Session> LoadAllSessions(string sessionsFolderPath)
    {
        // read all session from files
        var sessions = new ConcurrentDictionary<ulong, Session>();
        foreach (var filePath in Directory.GetFiles(sessionsFolderPath, $"*.{SessionFileExtension}")) {
            var session = JsonUtils.TryDeserializeFile<Session>(filePath);
            if (session == null) {
                VhLogger.Instance.LogError("Could not load session file. File: {File}", filePath);
                VhUtils.TryDeleteFile(filePath);
                continue;
            }

            sessions.TryAdd(session.SessionId, session);
        }

        return sessions;
    }

    private string GetSessionFilePath(ulong sessionId)
    {
        return Path.Combine(_sessionsFolderPath, $"{sessionId}.{SessionFileExtension}");
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        // timeout live session
        var minSessionTime = DateTime.UtcNow - _sessionPermanentlyTimeout;
        var minCloseWaitTime = DateTime.UtcNow - _sessionTemporaryTimeout;
        var timeoutSessions = Sessions
            .Where(x =>
                x.Value.EndTime == null && x.Value.LastUsedTime < minSessionTime ||
                x.Value.EndTime != null && x.Value.LastUsedTime < minCloseWaitTime);

        foreach (var item in timeoutSessions) {
            Sessions.TryRemove(item.Key, out _);
            VhUtils.TryDeleteFile(GetSessionFilePath(item.Key));
        }

        return default;
    }

    public string? FindTokenIdFromSessionId(ulong sessionId)
    {
        return Sessions.TryGetValue(sessionId, out var session) ? session.TokenId : null;
    }

    public SessionResponseEx CreateSession(SessionRequestEx sessionRequestEx, AccessTokenData accessTokenData)
    {
        //increment session id using atomic operation
        Interlocked.Increment(ref _lastSessionId);

        // create a new session
        var session = new Session {
            SessionId = (ulong)_lastSessionId,
            TokenId = accessTokenData.AccessToken.TokenId,
            ClientInfo = sessionRequestEx.ClientInfo,
            CreatedTime = FastDateTime.Now,
            LastUsedTime = FastDateTime.Now,
            SessionKey = VhUtils.GenerateKey(),
            ErrorCode = SessionErrorCode.Ok,
            HostEndPoint = sessionRequestEx.HostEndPoint,
            ClientIp = sessionRequestEx.ClientIp,
            ExtraData = sessionRequestEx.ExtraData,
            ProtocolVersion = sessionRequestEx.ProtocolVersion
        };

        // process plan id
        if (!IsUnitTest && sessionRequestEx.PlanId != ConnectPlanId.Normal) {
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.PlanRejected,
                ErrorMessage = "PlanId is not supported."
            };
        }

        var adRequirement = IsUnitTest
            ? ProcessPlanId(session, accessTokenData, sessionRequestEx.PlanId)
            : AdRequirement.None;

        //create response
        var responseEx = BuildSessionResponse(session, accessTokenData);

        // add session creation data
        responseEx.AdRequirement = adRequirement;
        responseEx.AccessInfo = new AccessInfo {
            ExpirationTime = accessTokenData.AccessToken.ExpirationTime,
            LastUsedTime = accessTokenData.Usage.LastUsedTime,
            CreatedTime = accessTokenData.Usage.CreatedTime,
            IsPremium = true, // token is always premium in File Access Manager
            MaxDeviceCount = accessTokenData.AccessToken.MaxClientCount,
            MaxTotalTraffic = accessTokenData.AccessToken.MaxTraffic,
            DevicesSummary = null // not supported
        };

        if (responseEx.ErrorCode != SessionErrorCode.Ok)
            return responseEx;

        // add the new session to collection
        Sessions.TryAdd(session.SessionId, session);
        File.WriteAllText(GetSessionFilePath(session.SessionId), JsonSerializer.Serialize(session));

        responseEx.SessionId = session.SessionId;
        return responseEx;
    }

    private AdRequirement ProcessPlanId(Session session, AccessTokenData accessTokenData, ConnectPlanId planId)
    {
        switch (planId) {
            // PremiumByRewardedAd is just used for test purpose in File Access Manager. 
            case ConnectPlanId.PremiumByRewardedAd:
                session.ExpirationTime = DateTime.UtcNow + _adRequiredTimeout;
                return AdRequirement.Rewarded;

            // ad is just used for test purpose in File Access Manager
            case ConnectPlanId.PremiumByTrial:
                session.ExpirationTime = DateTime.UtcNow + _trialTimeout;
                return AdRequirement.None;

            case ConnectPlanId.Normal:
            default:
                return accessTokenData.AccessToken.AdRequirement;
        }
    }

    public SessionResponseEx GetSessionResponse(ulong sessionId, AccessTokenData accessTokenData,
        IPEndPoint? hostEndPoint, bool? isValidAd = null)
    {
        // check existence
        if (!Sessions.TryGetValue(sessionId, out var session))
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Session does not exist."
            };

        if (hostEndPoint != null)
            session.HostEndPoint = hostEndPoint;

        // create response
        var ret = BuildSessionResponse(session, accessTokenData, isValidAd);

        // update session status
        if (session.EndTime == null && ret.ErrorCode != SessionErrorCode.Ok) {
            session.ErrorCode = ret.ErrorCode;
            session.EndTime = DateTime.UtcNow;
        }

        return ret;
    }

    public ulong[] ResetUpdatedSessions()
    {
        lock (_updatedSessionIds) {
            var sessionIds = _updatedSessionIds.Select(x => x.Key).ToArray();
            _updatedSessionIds.Clear();
            return sessionIds;
        }
    }

    private SessionResponseEx BuildSessionResponse(Session session, AccessTokenData accessTokenData,
        bool? isValidAd = null)
    {
        // check if the ad is valid. MUST before access usage
        if (isValidAd == true)
            session.ExpirationTime = null;

        // build access usage
        var accessToken = accessTokenData.AccessToken;
        var accessUsage = new AccessUsage {
            ActiveClientCount = 0,
            ExpirationTime = session.ExpirationTime,
            MaxTraffic = accessToken.MaxTraffic,
            CycleTraffic = new Traffic(accessTokenData.Usage.Sent, accessTokenData.Usage.Received),
            IsPremium = true // token is always premium in File Access Manager
        };


        if (isValidAd == false)
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.RewardedAdRejected,
                ErrorMessage = "Could not validate the RewardedAd.",
                AccessUsage = accessUsage
            };

        // validate session status
        // ReSharper disable once InvertIf
        if (session.ErrorCode == SessionErrorCode.Ok) {
            // check token expiration
            if (accessToken.ExpirationTime != null && accessToken.ExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx {
                    ErrorCode = SessionErrorCode.AccessExpired,
                    ErrorMessage = "Access Expired.",
                    AccessUsage = accessUsage
                };

            // session expiration
            if (session.ExpirationTime != null && session.ExpirationTime < DateTime.UtcNow)
                return new SessionResponseEx {
                    ErrorCode = SessionErrorCode.SessionExpired,
                    AccessUsage = accessUsage,
                    ErrorMessage = "Session Expired!"
                };

            // check traffic
            if (accessUsage.MaxTraffic != 0 &&
                accessUsage.CycleTraffic.Total > accessUsage.MaxTraffic)
                return new SessionResponseEx {
                    ErrorCode = SessionErrorCode.AccessTrafficOverflow,
                    ErrorMessage = "All traffic quota has been consumed.",
                    AccessUsage = accessUsage
                };

            var otherSessions = Sessions.Values
                .Where(x => x.EndTime == null && x.TokenId == session.TokenId)
                .OrderBy(x => x.CreatedTime).ToArray();

            // suppressedTo yourself
            var selfSessions = otherSessions.Where(x =>
                x.ClientInfo.ClientId == session.ClientInfo.ClientId && x.SessionId != session.SessionId).ToArray();
            if (selfSessions.Any()) {
                session.SuppressedTo = SessionSuppressType.YourSelf;
                foreach (var selfSession in selfSessions) {
                    selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                    selfSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    selfSession.Kill();
                    lock (_updatedSessionIds) {
                        _updatedSessionIds.TryAdd(selfSession.SessionId, true);
                    }
                }
            }

            if (accessTokenData.AccessToken.MaxClientCount != 0) {
                // suppressedTo others by MaxClientCount
                var otherSessions2 = otherSessions
                    .Where(x =>
                        x.ClientInfo.ClientId != session.ClientInfo.ClientId &&
                        x.SessionId != session.SessionId)
                    .OrderBy(x => x.CreatedTime).ToArray();

                for (var i = 0; i <= otherSessions2.Length - accessTokenData.AccessToken.MaxClientCount; i++) {
                    var otherSession = otherSessions2[i];
                    otherSession.SuppressedBy = SessionSuppressType.Other;
                    otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    otherSession.EndTime = FastDateTime.Now;
                    session.SuppressedTo = SessionSuppressType.Other;
                    lock (_updatedSessionIds) {
                        _updatedSessionIds.TryAdd(otherSession.SessionId, true);
                    }
                }
            }

            // set to session expiration time if session expiration time is shorter than accessUsage.ExpirationTime
            if (session.ExpirationTime != null && (accessUsage.ExpirationTime == null ||
                                                   session.ExpirationTime < accessUsage.ExpirationTime)) {
                accessUsage.ExpirationTime = session.ExpirationTime.Value;
            }

            accessUsage.ActiveClientCount = otherSessions
                .GroupBy(x => x.ClientInfo.ClientId)
                .Count();
        }

        // build result
        return new SessionResponseEx {
            SessionId = session.SessionId,
            CreatedTime = session.CreatedTime,
            SessionKey = session.SessionKey,
            SuppressedTo = session.SuppressedTo,
            SuppressedBy = session.SuppressedBy,
            ErrorCode = session.ErrorCode,
            ErrorMessage = session.ErrorMessage,
            AccessUsage = accessUsage,
            RedirectHostEndPoint = null,
            AdRequirement = accessToken.AdRequirement,
            ExtraData = session.ExtraData,
            ProtocolVersion = session.ProtocolVersion
        };
    }

    public void CloseSession(ulong sessionId, SessionErrorCode errorCode)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
            return;

        if (session.ErrorCode == SessionErrorCode.Ok)
            session.ErrorCode = errorCode;

        session.Kill();
    }

    public void Dispose()
    {
        _cleanupSessionsJob.Dispose();
    }
}