using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Server.Access.Managers.FileAccessManagers.Services;

public class SessionService : IDisposable, IJob
{
    private const string SessionFileExtension = "session";
    private readonly TimeSpan _sessionPermanentlyTimeout = TimeSpan.FromHours(48);
    private readonly TimeSpan _sessionTemporaryTimeout = TimeSpan.FromHours(20);
    private readonly TimeSpan _adRequiredTimeout = TimeSpan.FromMinutes(4);
    private ulong _lastSessionId;
    private readonly string _sessionsFolderPath;
    public JobSection JobSection { get; } = new();

    public ConcurrentDictionary<ulong, Session> Sessions { get; }

    public SessionService(string sessionsFolderPath)
    {
        JobRunner.Default.Add(this);
        _sessionsFolderPath = sessionsFolderPath;
        Directory.CreateDirectory(sessionsFolderPath);

        // load all previous sessions to dictionary
        Sessions = LoadAllSessions(sessionsFolderPath);
    }

    private static ConcurrentDictionary<ulong, Session> LoadAllSessions(string sessionsFolderPath)
    {
        // read all session from files
        var sessions = new ConcurrentDictionary<ulong, Session>();
        foreach (var filePath in Directory.GetFiles(sessionsFolderPath, $"*.{SessionFileExtension}")) {
            var session = VhUtil.JsonDeserializeFile<Session>(filePath);
            if (session == null) {
                VhLogger.Instance.LogError("Could not load session file. File: {File}", filePath);
                VhUtil.TryDeleteFile(filePath);
                continue;
            }

            sessions.TryAdd(session.SessionId, session);
        }

        return sessions;
    }

    public Task RunJob()
    {
        CleanupSessions();
        return Task.CompletedTask;
    }

    private string GetSessionFilePath(ulong sessionId)
    {
        return Path.Combine(_sessionsFolderPath, $"{sessionId}.{SessionFileExtension}");
    }

    private void CleanupSessions()
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
            VhUtil.TryDeleteFile(GetSessionFilePath(item.Key));
        }
    }

    public string? TokenIdFromSessionId(ulong sessionId)
    {
        return Sessions.TryGetValue(sessionId, out var session) ? session.TokenId : null;
    }

    private static bool ValidateRequest(SessionRequestEx sessionRequestEx, AccessTokenData accessTokenData)
    {
        var encryptClientId = VhUtil.EncryptClientId(sessionRequestEx.ClientInfo.ClientId, accessTokenData.AccessToken.Secret);
        return encryptClientId.SequenceEqual(sessionRequestEx.EncryptedClientId);
    }

    public SessionResponseEx CreateSession(SessionRequestEx sessionRequestEx, AccessTokenData accessTokenData)
    {
        // validate the request
        if (!ValidateRequest(sessionRequestEx, accessTokenData))
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Could not validate the request."
            };


        //increment session id using atomic operation
        lock (Sessions)
            _lastSessionId++;

        // create a new session
        var session = new Session {
            SessionId = _lastSessionId,
            TokenId = accessTokenData.AccessToken.TokenId,
            ClientInfo = sessionRequestEx.ClientInfo,
            CreatedTime = FastDateTime.Now,
            LastUsedTime = FastDateTime.Now,
            SessionKey = VhUtil.GenerateKey(),
            ErrorCode = SessionErrorCode.Ok,
            HostEndPoint = sessionRequestEx.HostEndPoint,
            ClientIp = sessionRequestEx.ClientIp,
            ExtraData = sessionRequestEx.ExtraData,
            ExpirationTime = accessTokenData.AccessToken.AdRequirement is AdRequirement.Required
                ? DateTime.UtcNow + _adRequiredTimeout
                : null
        };

        //create response
        var ret = BuildSessionResponse(session, accessTokenData);
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // add the new session to collection
        Sessions.TryAdd(session.SessionId, session);
        File.WriteAllText(GetSessionFilePath(session.SessionId), JsonSerializer.Serialize(session));

        ret.SessionId = session.SessionId;
        return ret;
    }

    public SessionResponseEx GetSession(ulong sessionId, AccessTokenData accessTokenData,
        IPEndPoint? hostEndPoint)
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
        var ret = BuildSessionResponse(session, accessTokenData);
        return ret;
    }

    public SessionResponseEx[] GetSessions(AccessToken?[] accessItems)
    {
        //var ret = Sessions.Values
        //    .Select(x => BuildSessionResponse(x, accessItems.f))
        //    .ToArray();

        //return ret;
        throw new NotImplementedException();
    }

    private SessionResponseEx BuildSessionResponse(Session session, AccessTokenData accessTokenData)
    {
        var accessToken = accessTokenData.AccessToken;
        var accessUsage = new AccessUsage {
            ActiveClientCount = 0,
            ExpirationTime = accessToken.ExpirationTime,
            MaxClientCount = accessToken.MaxClientCount,
            MaxTraffic = accessToken.MaxTraffic,
            Traffic = new Traffic(accessTokenData.Usage.Sent, accessTokenData.Usage.Received)
        };

        // validate session status
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
                    ErrorCode = SessionErrorCode.AccessExpired,
                    AccessUsage = accessUsage,
                    ErrorMessage = "Session Expired!"
                };

            // check traffic
            if (accessUsage.MaxTraffic != 0 &&
                accessUsage.Traffic.Total > accessUsage.MaxTraffic)
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
                }
            }

            if (accessUsage.MaxClientCount != 0) {
                // suppressedTo others by MaxClientCount
                var otherSessions2 = otherSessions
                    .Where(x => x.ClientInfo.ClientId != session.ClientInfo.ClientId &&
                                x.SessionId != session.SessionId)
                    .OrderBy(x => x.CreatedTime).ToArray();

                for (var i = 0; i <= otherSessions2.Length - accessUsage.MaxClientCount; i++) {
                    var otherSession = otherSessions2[i];
                    otherSession.SuppressedBy = SessionSuppressType.Other;
                    otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    otherSession.EndTime = FastDateTime.Now;
                    session.SuppressedTo = SessionSuppressType.Other;
                }
            }

            // set to session expiration time if session expiration time is shorter than accessUsage.ExpirationTime
            if (session.ExpirationTime != null && (accessUsage.ExpirationTime == null ||
                                                   session.ExpirationTime < accessUsage.ExpirationTime))
                accessUsage.ExpirationTime = session.ExpirationTime.Value;

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
            ExtraData = session.ExtraData
        };
    }

    public void CloseSession(ulong sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session))
            return;

        if (session.ErrorCode == SessionErrorCode.Ok)
            session.ErrorCode = SessionErrorCode.SessionClosed;

        session.Kill();
    }

    public void Dispose()
    {
    }
}