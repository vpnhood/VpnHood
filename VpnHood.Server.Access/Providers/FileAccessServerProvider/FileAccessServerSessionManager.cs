using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Common.JobController;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Messaging;

namespace VpnHood.Server.Providers.FileAccessServerProvider;

public class FileAccessServerSessionManager : IDisposable, IJob
{
    private readonly TimeSpan _sessionPermanentlyTimeout = TimeSpan.FromHours(48);
    private readonly TimeSpan _sessionTemporaryTimeout = TimeSpan.FromHours(20);
    private uint _lastSessionId;

    public ConcurrentDictionary<uint, Session> Sessions { get; } = new();

    public FileAccessServerSessionManager()
    {
        JobRunner.Default.Add(this);
    }

    public Task RunJob()
    {
        CleanupSessions();
        return Task.CompletedTask;
    }

    public JobSection JobSection { get; } = new ();

    private void CleanupSessions()
    {
        // timeout live session
        var minSessionTime = DateTime.UtcNow - _sessionPermanentlyTimeout;
        var minCloseWaitTime = DateTime.UtcNow - _sessionTemporaryTimeout;
        var timeoutSessions = Sessions
            .Where(x =>
                (x.Value.EndTime == null && x.Value.LastUsedTime < minSessionTime) ||
                (x.Value.EndTime != null && x.Value.LastUsedTime < minCloseWaitTime));

        foreach (var item in timeoutSessions)
            Sessions.TryRemove(item.Key, out _);
    }

    public Guid? TokenIdFromSessionId(uint sessionId)
    {
        return Sessions.TryGetValue(sessionId, out var session) ? session.TokenId : null;
    }

    private static bool ValidateRequest(SessionRequest sessionRequest, FileAccessServer.AccessItem accessItem)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, accessItem.Token.Secret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public SessionResponseEx CreateSession(SessionRequestEx sessionRequestEx,
        FileAccessServer.AccessItem accessItem)
    {
        // validate the request
        if (!ValidateRequest(sessionRequestEx, accessItem))
            return new SessionResponseEx(SessionErrorCode.AccessError)
            { ErrorMessage = "Could not validate the request!" };

        // create a new session
        Session session = new()
        {
            TokenId = accessItem.Token.TokenId,
            ClientInfo = sessionRequestEx.ClientInfo,
            CreatedTime = FastDateTime.Now,
            LastUsedTime = FastDateTime.Now,
            SessionKey = Util.GenerateSessionKey(),
            ErrorCode = SessionErrorCode.Ok,
            HostEndPoint = sessionRequestEx.HostEndPoint,
            ClientIp = sessionRequestEx.ClientIp
        };

        //create response
        var ret = BuildSessionResponse(session, accessItem);
        if (ret.ErrorCode != SessionErrorCode.Ok)
            return ret;

        // add the new session to collection
        session.SessionId = ++_lastSessionId;
        Sessions.TryAdd(session.SessionId, session);
        ret.SessionId = session.SessionId;

        return ret;
    }

    public SessionResponseEx GetSession(uint sessionId, FileAccessServer.AccessItem accessItem,
        IPEndPoint? hostEndPoint)
    {
        // check existence
        if (!Sessions.TryGetValue(sessionId, out var session))
            return new SessionResponseEx(SessionErrorCode.AccessError) { ErrorMessage = "Session does not exist!" };

        if (hostEndPoint != null)
            session.HostEndPoint = hostEndPoint;

        // create response
        var ret = BuildSessionResponse(session, accessItem);
        return ret;
    }

    private SessionResponseEx BuildSessionResponse(Session session, FileAccessServer.AccessItem accessItem)
    {
        var accessUsage = accessItem.AccessUsage;

        // validate session status
        if (session.ErrorCode == SessionErrorCode.Ok)
        {
            // check token expiration
            if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < FastDateTime.Now)
                return new SessionResponseEx(SessionErrorCode.AccessExpired)
                { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

            // check traffic
            if (accessUsage.MaxTraffic != 0 &&
                accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessUsage.MaxTraffic)
                return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

            var otherSessions = Sessions.Values
                .Where(x => x.EndTime == null && x.TokenId == session.TokenId)
                .OrderBy(x => x.CreatedTime).ToArray();

            // suppressedTo yourself
            var selfSessions = otherSessions.Where(x =>
                x.ClientInfo.ClientId == session.ClientInfo.ClientId && x.SessionId != session.SessionId).ToArray();
            if (selfSessions.Any())
            {
                session.SuppressedTo = SessionSuppressType.YourSelf;
                foreach (var selfSession in selfSessions)
                {
                    selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                    selfSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    selfSession.Kill();
                }
            }

            // suppressedTo others by MaxClientCount
            if (accessUsage.MaxClientCount != 0)
            {
                var otherSessions2 = otherSessions
                    .Where(x => x.ClientInfo.ClientId != session.ClientInfo.ClientId &&
                                x.SessionId != session.SessionId)
                    .OrderBy(x => x.CreatedTime).ToArray();
                for (var i = 0; i <= otherSessions2.Length - accessUsage.MaxClientCount; i++)
                {
                    var otherSession = otherSessions2[i];
                    otherSession.SuppressedBy = SessionSuppressType.Other;
                    otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    otherSession.EndTime = FastDateTime.Now;
                    session.SuppressedTo = SessionSuppressType.Other;
                }
            }

            accessUsage.ActiveClientCount = 0;
        }

        // build result
        return new SessionResponseEx(SessionErrorCode.Ok)
        {
            SessionId = session.SessionId,
            CreatedTime = session.CreatedTime,
            SessionKey = session.SessionKey,
            SuppressedTo = session.SuppressedTo,
            SuppressedBy = session.SuppressedBy,
            ErrorCode = session.ErrorCode,
            ErrorMessage = session.ErrorMessage,
            AccessUsage = accessUsage,
            RedirectHostEndPoint = null
        };
    }

    public void CloseSession(uint sessionId)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            if (session.ErrorCode == SessionErrorCode.Ok)
                session.ErrorCode = SessionErrorCode.SessionClosed;
            session.Kill();
        }
    }

    public class Session
    {
        public uint SessionId { get; internal set; }
        public Guid TokenId { get; internal set; }
        public ClientInfo ClientInfo { get; internal set; } = null!;
        public byte[] SessionKey { get; internal set; } = null!;
        public DateTime CreatedTime { get; internal set; } = FastDateTime.Now;
        public DateTime LastUsedTime { get; internal set; } = FastDateTime.Now;
        public DateTime? EndTime { get; internal set; }
        public bool IsAlive => EndTime == null;
        public SessionSuppressType SuppressedBy { get; internal set; }
        public SessionSuppressType SuppressedTo { get; internal set; }
        public SessionErrorCode ErrorCode { get; internal set; }
        public string? ErrorMessage { get; internal set; }
        public IPEndPoint HostEndPoint { get; internal set; } = null!;
        public IPAddress? ClientIp { get; internal set; }

        public void Kill()
        {
            if (IsAlive)
                EndTime = FastDateTime.Now;
        }
    }

    public void Dispose()
    {
    }
}