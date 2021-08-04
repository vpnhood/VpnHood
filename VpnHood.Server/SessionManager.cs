using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling.Messages;
using VpnHood.Common.Trackers;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling;
using System.Threading;

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private const int Session_TimeoutSeconds = 10 * 60;
        private const int SendStatus_IntervalSeconds = 5 * 60;
        private readonly ConcurrentDictionary<int, SessionException> _sessionExceptions = new();
        private readonly ConcurrentDictionary<int, Session> _sessions = new();
        private readonly SocketFactory _socketFactory;
        private readonly ITracker _tracker;
        private readonly Timer _sendStatusTimer;
        private DateTime _lastCleanupTime = DateTime.MinValue;

        private IAccessServer AccessServer { get; }
        public int MaxDatagramChannelCount { get; set; } = TunnelUtil.MaxDatagramChannelCount;
        public string ServerVersion { get; }

        public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker tracker)
        {
            AccessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _tracker = tracker;
            _sendStatusTimer = new Timer(SendStatusToAccessServer, null, 0, SendStatus_IntervalSeconds * 1000);
            ServerVersion = typeof(TcpHost).Assembly.GetName().Version.ToString();
        }

        private void SendStatusToAccessServer(object _)
        {
            AccessServer.SendServerStatus(new ServerStatus { SessionCount = _sessions.Count });

            // report to console
            if (VhLogger.IsDiagnoseMode)
                ReportStatus();
        }

        public Session FindSessionByClientId(Guid clientId)
        {
            var session = _sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientInfo.ClientId == clientId).Value;
            if (session == null)
                throw new KeyNotFoundException($"Invalid clientId! ClientId: {clientId}");

            return GetSessionById(session.SessionId);
        }

        public Session GetSessionById(int sessionId)
        {
            // find in disposed exceptions
            if (_sessionExceptions.TryGetValue(sessionId, out var sessionException))
                throw sessionException;

            // find in session
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new SessionException(accessUsage: null,
                    responseCode: ResponseCode.InvalidSessionId,
                    suppressedBy: SuppressType.None,
                    message: $"Invalid SessionId, SessionId: {VhLogger.FormatSessionId(sessionId)}");

            // check session status
            if (!session.IsDisposed)
                session.UpdateStatus();

            if (session.IsDisposed)
                throw RemoveSession(session);

            return session;
        }

        private static SessionException CreateDisposedSessionException(Session session)
        {
            var responseCode = session.SuppressedBy != SuppressType.None ? ResponseCode.SessionSuppressedBy : ResponseCode.SessionClosed;
            var accessError = session.AccessController.ResponseCode != ResponseCode.Ok;
            if (accessError) responseCode = session.AccessController.ResponseCode;

            return new SessionException(
                accessUsage: session.AccessController.AccessUsage,
                responseCode: responseCode,
                suppressedBy: session.SuppressedBy,
                message: accessError ? session.AccessController.Access.Message : "Session has been closed"
                );
        }

        internal Session GetSession(SessionRequest sessionRequest)
        {
            //get session
            var session = GetSessionById(sessionRequest.SessionId);

            //todo: remove if from 1.1.243 and upper. sessionRequest.SessionKey must not null and valid
            if (sessionRequest.SessionKey != null && !sessionRequest.SessionKey.SequenceEqual(session.SessionKey))
                return session;

            return session;
        }

        public async Task<Session> CreateSession(HelloRequest helloRequest, IPEndPoint requestEndPoint, IPAddress clientIp)
        {
            // create the identity
            AccessRequest accessRequest = new()
            {
                TokenId = helloRequest.TokenId,
                ClientInfo = new ClientInfo()
                {
                    ClientId = helloRequest.ClientId,
                    ClientIp = clientIp,
                    UserAgent = helloRequest.UserAgent,
                    UserToken = helloRequest.UserToken,
                    ClientVersion = helloRequest.ClientVersion
                },
                RequestEndPoint = requestEndPoint
            };

            // validate the token
            VhLogger.Instance.Log(LogLevel.Trace, $"Validating the request. TokenId: {VhLogger.FormatId(helloRequest.TokenId)}");
            var accessController = await AccessController.Create(AccessServer, accessRequest, helloRequest.EncryptedClientId);

            // cleanup old timeout sessions
            Cleanup();

            // first: suppress a session of same client if maxClient is exceeded
            var oldSession = _sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientInfo.ClientId == helloRequest.ClientId).Value;

            // second: suppress a session of other with same accessId if MaxClientCount is exceeded. MaxClientCount zero means unlimited 
            if (oldSession == null && accessController.Access.MaxClientCount > 0)
            {
                var otherSessions = _sessions.Where(x => !x.Value.IsDisposed && x.Value.AccessController.Access.AccessId == accessController.Access.AccessId)
                    .OrderBy(x => x.Value.CreatedTime).ToArray();
                if (otherSessions.Length >= accessController.Access.MaxClientCount)
                    oldSession = otherSessions[0].Value;
            }

            if (oldSession != null)
            {
                VhLogger.Instance.LogInformation($"Suppressing other session. SuppressedClientId: {VhLogger.FormatId(oldSession.ClientInfo.ClientId)}, SuppressedSessionId: {VhLogger.FormatSessionId(oldSession.SessionId)}");
                oldSession.SuppressedByClientId = helloRequest.ClientId;
                oldSession.Dispose();
            }

            // create new session
            var session = new Session(accessController, _socketFactory, Session_TimeoutSeconds, MaxDatagramChannelCount)
            {
                SuppressedToClientId = oldSession?.ClientInfo.ClientId
            };
            _sessions.TryAdd(session.SessionId, session);
            _tracker?.TrackEvent("Usage", "SessionCreated").GetAwaiter();
            VhLogger.Instance.Log(LogLevel.Information, $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            return session;
        }

        public void ReportStatus()
        {
            Cleanup(true);
            var msg = $"*** ReportStatus ***, ";
            msg += $"ActiveSessionCount: {_sessions.Count(x => !x.Value.IsDisposed)}, ";
            msg += $"DisposedSessionCount: {_sessions.Count(x => x.Value.IsDisposed)}, ";
            msg += $"TotalDatagramChannel: {_sessions.Sum(x => x.Value.Tunnel.DatagramChannels.Length)}";
            VhLogger.Instance.LogInformation(msg);
        }

        private void Cleanup(bool force = false)
        {
            if (!force && (DateTime.Now - _lastCleanupTime).TotalSeconds < Session_TimeoutSeconds)
                return;
            _lastCleanupTime = DateTime.Now;

            // update all sessions satus
            foreach (var session in _sessions.Where(x => !x.Value.IsDisposed))
                session.Value.UpdateStatus();

            // removing disposed sessions
            VhLogger.Instance.Log(LogLevel.Trace, $"Removing timeout sessions...");
            var disposedSessions = _sessions.Where(x => x.Value.IsDisposed);
            foreach (var item in disposedSessions)
                RemoveSession(item.Value);

            // remove old sessionExceptions
            var oldSessionExceptions = _sessionExceptions.Where(x => (DateTime.Now - x.Value.CreatedTime).TotalSeconds > Session_TimeoutSeconds);
            foreach (var item in oldSessionExceptions)
                _sessionExceptions.TryRemove(item.Key, out var _);

            // free server memory now
            GC.Collect();
        }

        private SessionException RemoveSession(Session session)
        {
            // remove session
            if (!session.IsDisposed)
                session.Dispose();
            _sessions.TryRemove(session.SessionId, out _);

            // add to sessionExceptions
            var sessionException = CreateDisposedSessionException(session);
            _sessionExceptions.TryAdd(session.SessionId, sessionException);
            VhLogger.Instance.Log(LogLevel.Information, $"Session has been removed! ClientId: {VhLogger.FormatId(session.ClientInfo.ClientId)}, SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            return sessionException;
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
                session.Dispose();
            _sendStatusTimer.Dispose();
        }
    }
}