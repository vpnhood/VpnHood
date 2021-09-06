using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Trackers;
using VpnHood.Common.Logging;
using VpnHood.Server.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private readonly IAccessServer _accessServer;
        private readonly long _accessSyncCacheSize;
        private readonly Timer _cleanUpTimer;
        private readonly SocketFactory _socketFactory;
        private readonly ITracker? _tracker;
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(10); //after that session can be recovered by access server

        public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker? tracker,
            long accessSyncCacheSize)
        {
            _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _tracker = tracker;
            _accessSyncCacheSize = accessSyncCacheSize;
            _cleanUpTimer = new Timer(_ => Cleanup(), null, _sessionTimeout, _sessionTimeout);
            ServerVersion = typeof(SessionManager).Assembly.GetName().Version.ToString();
        }

        public int MaxDatagramChannelCount { get; set; } = TunnelUtil.MaxDatagramChannelCount;
        public string ServerVersion { get; }
        public ConcurrentDictionary<uint, Session> Sessions { get; } = new();

        public void Dispose()
        {
            _cleanUpTimer.Dispose();
            foreach (var session in Sessions.Values)
                session.Dispose();
        }

        private Session CreateSession(SessionResponse sessionResponse)
        {
            VerifySessionResponse(sessionResponse);

            var session = new Session(
                _accessServer,
                sessionResponse,
                _socketFactory,
                MaxDatagramChannelCount,
                _accessSyncCacheSize);

            Sessions.TryAdd(session.SessionId, session);
            return session;
        }

        public async Task<SessionResponse> CreateSession(HelloRequest helloRequest, IPEndPoint hostEndPoint,
            IPAddress clientIp)
        {
            // validate the token
            VhLogger.Instance.Log(LogLevel.Trace,
                $"Validating the request. TokenId: {VhLogger.FormatId(helloRequest.TokenId)}");
            var sessionResponse = await _accessServer.Session_Create(new SessionRequestEx(helloRequest, hostEndPoint)
                {ClientIp = clientIp});
            var session = CreateSession(sessionResponse);
            session.UseUdpChannel = true;

            _ = _tracker?.TrackEvent("Usage", "SessionCreated");
            VhLogger.Instance.Log(LogLevel.Information,
                $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
            return sessionResponse;
        }

        internal async Task<Session> GetSession(RequestBase sessionRequest, IPEndPoint hostEndPoint,
            IPAddress? clientIp)
        {
            //get session
            var session = GetSessionById(sessionRequest.SessionId);
            if (session != null)
            {
                if (!sessionRequest.SessionKey.SequenceEqual(session.SessionKey))
                    throw new UnauthorizedAccessException("Invalid SessionKey");
            }
            // try to restore session if not found
            else
            {
                var sessionResponse = await _accessServer.Session_Get(sessionRequest.SessionId, hostEndPoint, clientIp);
                if (!sessionRequest.SessionKey.SequenceEqual(sessionRequest.SessionKey))
                    throw new UnauthorizedAccessException("Invalid SessionKey");

                session = CreateSession(sessionResponse);
            }

            // any session Exception must be after validation
            VerifySessionResponse(session.SessionResponse);

            // session that just removed from this server should not be exist at all so dispose state means 
            if (session.IsDisposed)
                throw new SessionException(SessionErrorCode.SessionClosed);

            return session;
        }

        public Session? GetSessionById(uint sessionId)
        {
            Sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        private static void VerifySessionResponse(ResponseBase sessionResponse)
        {
            if (sessionResponse.ErrorCode == SessionErrorCode.GeneralError)
                throw new UnauthorizedAccessException(sessionResponse.ErrorMessage);

            if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
                throw new SessionException(sessionResponse);
        }

        private void Cleanup()
        {
            // update all sessions status
            foreach (var item in Sessions.Where(x => DateTime.Now - x.Value.LastActivityTime > _sessionTimeout)
                .ToArray())
            {
                item.Value.Dispose();
                Sessions.Remove(item.Key, out _);
            }
        }

        /// <summary>
        ///     Close session in this server and AccessServer
        /// </summary>
        /// <param name="sessionId"></param>
        public void CloseSession(uint sessionId)
        {
            // find in session
            if (!Sessions.TryGetValue(sessionId, out var session))
                return;
            session.Dispose(true);
        }
    }
}