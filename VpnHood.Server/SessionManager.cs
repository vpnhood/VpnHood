using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling.Messages;
using VpnHood.Common.Trackers;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling;

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private const int Session_TimeoutSeconds = 10 * 60;
        private readonly ConcurrentDictionary<int, SessionException> _sessionExceptions = new();
        private readonly SocketFactory _socketFactory;
        private readonly ITracker? _tracker;
        private readonly long _accessSyncCacheSize;
        private DateTime _lastCleanupTime = DateTime.MinValue;

        private IAccessServer AccessServer { get; }
        public int MaxDatagramChannelCount { get; set; } = TunnelUtil.MaxDatagramChannelCount;
        public string ServerVersion { get; }
        public ConcurrentDictionary<int, Session> Sessions { get; } = new();

        public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker? tracker, long accessSyncCacheSize)
        {
            AccessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _tracker = tracker;
            _accessSyncCacheSize = accessSyncCacheSize;
            ServerVersion = typeof(TcpHost).Assembly.GetName().Version.ToString();
        }

        public Session FindSessionByClientId(Guid clientId)
        {
            var session = Sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientInfo.ClientId == clientId).Value;
            if (session == null)
                throw new KeyNotFoundException($"Invalid clientId! ClientId: {clientId}");

            return GetSessionById(session.SessionId);
        }

        public Session GetSessionById(int sessionId)
        {

            // find in session
            if (!Sessions.TryGetValue(sessionId, out var session))
            {
                // find in disposed exceptions
                throw _sessionExceptions.TryGetValue(sessionId, out var sessionException)
                    ? sessionException
                    : new SessionException(responseCode: ResponseCode.InvalidSessionId, message: $"Invalid SessionId, SessionId: {VhLogger.FormatSessionId(sessionId)}");
            }

            // check session status
            if (session.IsDisposed)
                throw RemoveSession(session);
            else
                session.UpdateStatus();

            return session;
        }

        internal Session GetSession(SessionRequest sessionRequest)
        {
            //get session
            var session = GetSessionById(sessionRequest.SessionId);

            if (!sessionRequest.SessionKey.SequenceEqual(session.SessionKey))
                throw new Exception("Invalid SessionKey");

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
            var accessController = await AccessController.Create(AccessServer, accessRequest, helloRequest.EncryptedClientId, syncCacheSize: _accessSyncCacheSize);

            // cleanup old timeout sessions
            Cleanup();

            // first: suppress a session of same client if maxClient is exceeded
            var oldSession = Sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientInfo.ClientId == helloRequest.ClientId).Value;

            // second: suppress a session of other with same accessId if MaxClientCount is exceeded. MaxClientCount zero means unlimited 
            if (oldSession == null && accessController.Access.MaxClientCount > 0)
            {
                var otherSessions = Sessions.Where(x => !x.Value.IsDisposed && x.Value.AccessController.Access.AccessId == accessController.Access.AccessId)
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
            Sessions.TryAdd(session.SessionId, session);
            _ = _tracker?.TrackEvent("Usage", "SessionCreated");
            VhLogger.Instance.Log(LogLevel.Information, $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            return session;
        }

        private void Cleanup(bool force = false)
        {
            if (!force && (DateTime.Now - _lastCleanupTime).TotalSeconds < Session_TimeoutSeconds)
                return;
            _lastCleanupTime = DateTime.Now;

            // update all sessions satus
            foreach (var session in Sessions.Where(x => !x.Value.IsDisposed))
                session.Value.UpdateStatus();

            // removing disposed sessions
            VhLogger.Instance.Log(LogLevel.Trace, $"Removing timeout sessions...");
            var disposedSessions = Sessions.Where(x => x.Value.IsDisposed);
            foreach (var item in disposedSessions)
                RemoveSession(item.Value);

            // remove old sessionExceptions
            var oldSessionExceptions = _sessionExceptions.Where(x => (DateTime.Now - x.Value.CreatedTime).TotalSeconds > Session_TimeoutSeconds);
            foreach (var item in oldSessionExceptions)
                _sessionExceptions.TryRemove(item.Key, out var _);
        }

        private SessionException RemoveSession(Session session)
        {
            // remove session
            session.Dispose();
            if (Sessions.TryRemove(session.SessionId, out _))
                VhLogger.Instance.Log(LogLevel.Information, $"Session has been removed! ClientId: {VhLogger.FormatId(session.ClientInfo.ClientId)}, SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            if (session.SuppressedBy != SuppressType.None)
                return new SessionException(session.SuppressedBy, session.AccessController.AccessUsage);
            else if (session.AccessController.ResponseCode != ResponseCode.Ok)
                return new SessionException(session.AccessController.ResponseCode, session.AccessController.AccessUsage, session.AccessController.Access.Message);
            else
                return new SessionException(ResponseCode.SessionClosed, session.AccessController.AccessUsage, "Session has been closed");
        }

        public void Dispose()
        {
            foreach (var session in Sessions.Values)
                session.Dispose();
        }
    }
}