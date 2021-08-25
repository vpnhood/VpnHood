using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using VpnHood.Common;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.AccessServers
{

    public class FileAccessServerSessionManager : IDisposable
    {
        public class Session
        {
            public uint SessionId { get; internal set; }
            public Guid TokenId { get; internal set; }
            public ClientInfo ClientInfo { get; internal set; } = null!;
            public byte[] SessionKey { get; internal set; } = null!;
            public DateTime CreatedTime { get; internal set; } = DateTime.Now;
            public DateTime AccessedTime { get; internal set; } = DateTime.Now;
            public DateTime? DeadTime { get; internal set; }
            public bool IsAlive => DeadTime == null;
            public SessionSuppressType SuppressedBy { get; internal set; }
            public SessionSuppressType SuppressedTo { get; internal set; }
            public SessionErrorCode ErrorCode { get; internal set; }
            public string? ErrorMessage { get; internal set; }
            public IPEndPoint HostEndPoint { get; internal set; } = null!;
            public IPAddress? ClientIp { get; internal set; }

            public void Kill()
            {
                if (IsAlive)
                    DeadTime = DateTime.Now;
            }
        }

        private readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(60);
        private uint _lastSessionId = 0;
        private readonly System.Threading.Timer _cleanupTimer;

        public ConcurrentDictionary<uint, Session> Sessions { get; } = new();

        public FileAccessServerSessionManager()
        {
            _cleanupTimer = new System.Threading.Timer(CleanupSessions, null, 0, (int)SessionTimeout.TotalMilliseconds / 3);
        }

        private void CleanupSessions(object state)
        {
            // timeout live session
            var timeoutSessions = Sessions.Where(x => DateTime.Now - x.Value.AccessedTime > SessionTimeout).ToArray();
            foreach (var item in timeoutSessions)
                Sessions.TryRemove(item.Key, out _);
        }

        public Guid? TokenIdFromSessionId(uint sessionId)
            => Sessions.TryGetValue(sessionId, out var session) ? session.TokenId : null;

        private bool ValidateRequest(SessionRequest sessionRequest, FileAccessServer.AccessItem accessItem)
        {
            var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, accessItem.Token.Secret);
            return Enumerable.SequenceEqual(encryptClientId, sessionRequest.EncryptedClientId);
        }

        public SessionResponseEx CreateSession(SessionRequestEx sessionRequestEx, FileAccessServer.AccessItem accessItem)
        {
            var tokenId = sessionRequestEx.TokenId;
            var accessUsage = accessItem.AccessUsage;

            // validate the request
            if (!ValidateRequest(sessionRequestEx, accessItem))
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Could not validate the request!" };

            // check token expiration
            if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.Now)
                return new(SessionErrorCode.AccessExpired) { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

            // check traffic
            else if (accessUsage.MaxTraffic != 0 && accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessUsage.MaxTraffic)
                return new(SessionErrorCode.AccessTrafficOverflow) { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

            // create a new session
            Session session = new()
            {
                SessionId = ++_lastSessionId,
                TokenId = accessItem.Token.TokenId,
                ClientInfo = sessionRequestEx.ClientInfo,
                CreatedTime = DateTime.Now,
                AccessedTime = DateTime.Now,
                SessionKey = Util.GenerateSessionKey(),
                ErrorCode = SessionErrorCode.Ok,
                HostEndPoint = sessionRequestEx.HostEndPoint,
                ClientIp = sessionRequestEx.ClientIp
            };

            // suppressedTo yourself
            var selfSessions = Sessions.Where(x => x.Value.IsAlive && x.Value.TokenId == tokenId && x.Value.ClientInfo.ClientId == sessionRequestEx.ClientInfo.ClientId);
            if (selfSessions.Any())
            {
                session.SuppressedTo = SessionSuppressType.YourSelf;
                foreach (var selfSession in selfSessions)
                {
                    selfSession.Value.SuppressedBy = SessionSuppressType.YourSelf;
                    selfSession.Value.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    selfSession.Value.Kill();
                }
            }

            // suppressedTo others by MaxClientCount
            if (accessUsage.MaxClientCount != 0)
            {
                var otherSessions = Sessions
                    .Where(x => x.Value.IsAlive && x.Value.TokenId == tokenId)
                    .OrderBy(x => x.Value.CreatedTime).ToArray();
                for (var i = 0; i <= otherSessions.Length - accessUsage.MaxClientCount; i++)
                {
                    var otherSession = otherSessions[i];
                    otherSession.Value.SuppressedBy = SessionSuppressType.Other;
                    otherSession.Value.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                    session.SuppressedTo = SessionSuppressType.Other;
                    otherSession.Value.Kill();
                }
            }

            // add the new session to collection
            Sessions.TryAdd(session.SessionId, session);

            //c reate response
            var ret = BuidSessionResponse(session, accessItem);
            return ret;
        }

        public SessionResponseEx GetSession(uint sessionId, FileAccessServer.AccessItem accessItem, IPEndPoint? hostEndPoint)
        {
            // check existance
            if (!Sessions.TryGetValue(sessionId, out var session))
                return new SessionResponseEx(SessionErrorCode.GeneralError) { ErrorMessage = "Session does not exist!" };

            if (hostEndPoint != null)
                session.HostEndPoint = hostEndPoint;

            // get usage of accessItem
            var accessUsage = accessItem.AccessUsage;

            // check expiration
            if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.Now)
            {
                session.ErrorMessage = "Access Expired!";
                session.ErrorCode = SessionErrorCode.AccessExpired;
                session.Kill();
            }
            // check traffic
            else if (accessItem.MaxTraffic != 0 && accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessItem.MaxTraffic)
            {
                session.ErrorMessage = "All traffic quota has been consumed!";
                session.ErrorCode = SessionErrorCode.AccessTrafficOverflow;
                session.Kill();
            }

            // create response
            var ret = BuidSessionResponse(session, accessItem);
            return ret;
        }

        private SessionResponseEx BuidSessionResponse(Session session, FileAccessServer.AccessItem accessItem)
        {
            session.AccessedTime = DateTime.Now;

            var accessUsage = accessItem.AccessUsage;
            return new SessionResponseEx(SessionErrorCode.SessionClosed)
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

        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }
    }
}
