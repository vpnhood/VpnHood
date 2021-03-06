﻿using Microsoft.Extensions.Logging;
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

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<int, SessionException> _sessionExceptions = new();
        private readonly ConcurrentDictionary<int, Session> _sessions = new();
        private readonly SocketFactory _socketFactory;
        private readonly ITracker _tracker;
        private const int SESSION_TimeoutSeconds = 10 * 60;
        private DateTime _lastCleanupTime = DateTime.MinValue;
        private IAccessServer AccessServer { get; }

        public string ServerId { get; }
        public string ServerVersion { get; }

        public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker tracker, string serverId)
        {
            AccessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _tracker = tracker;
            ServerId = serverId;
            ServerVersion = typeof(TcpHost).Assembly.GetName().Version.ToString();
        }

        public Session FindSessionByClientId(Guid clientId)
        {
            var session = _sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientId == clientId).Value;
            if (session == null)
                throw new KeyNotFoundException($"Invalid clientId! ClientId: {clientId}");

            return GetSessionById(session.SessionId);
        }

        public Session GetSessionById(int sessionId)
        {
            // find in disposed exceptions
            if (_sessionExceptions.TryGetValue(sessionId, out SessionException sessionException))
                throw sessionException;

            // find in session
            if (!_sessions.TryGetValue(sessionId, out Session session))
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
            bool accessError = session.AccessController.ResponseCode != ResponseCode.Ok;
            if (accessError) responseCode = session.AccessController.ResponseCode;

            return new SessionException(
                accessUsage: session.AccessController.AccessUsage,
                responseCode: responseCode,
                suppressedBy: session.SuppressedBy,
                message: accessError ? session.AccessController.Access.Message : "Session has been closed"
                );
        }

        public Session GetSession(SessionRequest sessionRequest)
        {
            //get session
            var session = GetSessionById(sessionRequest.SessionId);

            //todo: remove if from 1.1.243 and upper. sessionRequest.SessionKey must not null and valid
            if (sessionRequest.SessionKey != null && !sessionRequest.SessionKey.SequenceEqual(session.SessionKey)) 
                return session;

            return session;
        }

        public async Task<Session> CreateSession(HelloRequest helloRequest, IPAddress clientIp)
        {
            // create the identity
            var clientIdentity = new ClientIdentity()
            {
                ClientId = helloRequest.ClientId,
                ClientIp = clientIp.ToString(),
                TokenId = helloRequest.TokenId,
                UserToken = helloRequest.UserToken,
                ClientVersion = helloRequest.ClientVersion
            };

            // validate the token
            VhLogger.Instance.Log(LogLevel.Trace, $"Validating the request. TokenId: {VhLogger.FormatId(clientIdentity.TokenId)}");
            var accessController = await GetValidatedAccess(clientIdentity, helloRequest.EncryptedClientId);

            // cleanup old timeout sessions
            Cleanup();

            // first: suppress a session of same client if maxClient is exceeded
            var oldSession = _sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientId == clientIdentity.ClientId).Value;

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
                VhLogger.Instance.LogInformation($"Suppressing other session. SuppressedClientId: {VhLogger.FormatId(oldSession.ClientId)}, SuppressedSessionId: {VhLogger.FormatSessionId(oldSession.SessionId)}");
                oldSession.SuppressedByClientId = clientIdentity.ClientId;
                oldSession.Dispose();
            }

            // create new session
            var session = new Session(clientIdentity, accessController, _socketFactory, timeout: SESSION_TimeoutSeconds)
            {
                SuppressedToClientId = oldSession?.ClientId
            };
            _sessions.TryAdd(session.SessionId, session);
            _tracker?.TrackEvent("Usage", "SessionCreated").GetAwaiter();
            VhLogger.Instance.Log(LogLevel.Information, $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            return session;
        }

        private async Task<AccessController> GetValidatedAccess(ClientIdentity clientIdentity, byte[] encryptedClientId)
        {
            // get access
            var access = await AccessServer.GetAccess(clientIdentity);
            if (access == null)
                throw new Exception($"Could not find the tokenId! {VhLogger.FormatId(clientIdentity.TokenId)}, ClientId: {VhLogger.FormatId(clientIdentity.ClientId)}");

            // Validate token by shared secret
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = access.Secret;
            aes.IV = new byte[access.Secret.Length];
            aes.Padding = PaddingMode.None;
            using var cryptor = aes.CreateEncryptor();
            var ecid = cryptor.TransformFinalBlock(clientIdentity.ClientId.ToByteArray(), 0, clientIdentity.ClientId.ToByteArray().Length);
            if (!Enumerable.SequenceEqual(ecid, encryptedClientId))
                throw new Exception($"The request does not have a valid signature for requested token! {VhLogger.FormatId(clientIdentity.TokenId)}, ClientId: {VhLogger.FormatId(clientIdentity.ClientId)}");

            // find AccessController or Create
            var accessController =
                _sessions.FirstOrDefault(x => x.Value.AccessController.Access.AccessId == access.AccessId).Value?.AccessController
                ?? new AccessController(clientIdentity, AccessServer, access);

            accessController.Access = access; // update access control
            accessController.UpdateStatusCode();

            // check access
            if (accessController.Access.StatusCode != AccessStatusCode.Ok)
                throw new SessionException(
                    accessUsage: accessController.AccessUsage,
                    responseCode: accessController.ResponseCode,
                    suppressedBy: SuppressType.None,
                    message: accessController.Access.Message
                    );

            return accessController;
        }

        public void ReportStatus()
        {
            Cleanup(true);
            string msg = $"*** ReportStatus ***, ";
            msg += $"ActiveSessionCount: {_sessions.Count(x => !x.Value.IsDisposed)}, ";
            msg += $"DisposedSessionCount: {_sessions.Count(x => x.Value.IsDisposed)}, ";
            msg += $"TotalDatagramChannel: {_sessions.Sum(x => x.Value.Tunnel.DatagramChannels.Length)}";
            VhLogger.Instance.LogInformation(msg);
        }

        private void Cleanup(bool force = false)
        {
            if (!force && (DateTime.Now - _lastCleanupTime).TotalSeconds < SESSION_TimeoutSeconds)
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
            var oldSessionExceptions = _sessionExceptions.Where(x => (DateTime.Now - x.Value.CreatedTime).TotalSeconds > SESSION_TimeoutSeconds);
            foreach (var item in oldSessionExceptions)
                _sessionExceptions.TryRemove(item.Key, out SessionException _);

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
            VhLogger.Instance.Log(LogLevel.Information, $"Session has been removed! ClientId: {VhLogger.FormatId(session.ClientId)}, SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

            return sessionException;
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
                session.Dispose();
        }
    }
}