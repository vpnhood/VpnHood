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
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server
{

    public class SessionManager : IDisposable
    {
        private readonly IAccessServer _accessServer;
        private readonly Timer _cleanUpTimer;
        private readonly SocketFactory _socketFactory;
        private readonly ITracker? _tracker;

        public string ServerVersion { get; }
        public ConcurrentDictionary<uint, Session> Sessions { get; } = new();
        public TrackingOptions TrackingOptions { get; set; } = new();
        public SessionOptions SessionOptions { get; set; } = new();
        public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker? tracker)
        {
            _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _tracker = tracker;
            _cleanUpTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            ServerVersion = typeof(SessionManager).Assembly.GetName().Version.ToString();
        }

        public void Dispose()
        {
            _cleanUpTimer.Dispose();
            foreach (var session in Sessions.Values)
                session.Dispose();
        }

        private Session CreateSession(SessionResponse sessionResponse, IPEndPoint hostEndPoint)
        {
            VerifySessionResponse(sessionResponse);

            var session = new Session(
                _accessServer,
                sessionResponse,
                _socketFactory,
                hostEndPoint,
                SessionOptions,
                TrackingOptions);

            if (!Sessions.TryAdd(session.SessionId, session))
            {
                session.Dispose(true);
                throw new Exception($"Could not add session to collection: SessionId: {session.SessionId}");
            }

            return session;
        }

        public async Task<SessionResponse> CreateSession(HelloRequest helloRequest, IPEndPoint hostEndPoint,
            IPAddress clientIp)
        {
            // validate the token
            VhLogger.Instance.Log(LogLevel.Trace,
                $"Validating the request. TokenId: {VhLogger.FormatId(helloRequest.TokenId)}");
            var sessionResponse = await _accessServer.Session_Create(new SessionRequestEx(helloRequest, hostEndPoint)
            {
                ClientIp = clientIp
            });
            var session = CreateSession(sessionResponse, hostEndPoint);
            session.UseUdpChannel = true;

            _ = _tracker?.TrackEvent("Usage", "SessionCreated");
            VhLogger.Instance.Log(LogLevel.Information,
                $"New session has been created. SessionId: {VhLogger.FormatId(session.SessionId)}");
            return sessionResponse;
        }

        internal async Task<Session> GetSession(RequestBase sessionRequest, IPEndPoint hostEndPoint, IPAddress? clientIp)
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

                session = CreateSession(sessionResponse, hostEndPoint);
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
            var minSessionActivityTime = DateTime.Now - SessionOptions.Timeout;
            var timeoutSessions = Sessions.Where(x => x.Value.IsDisposed || x.Value.LastActivityTime < minSessionActivityTime).ToArray();
            foreach (var session in timeoutSessions)
            {
                session.Value.Dispose();
                Sessions.Remove(session.Key, out _);
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