using VpnHood.Server.Factory;
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
using System.Threading;

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<ulong, SessionException> _sessionExceptions = new ConcurrentDictionary<ulong, SessionException>();
        private readonly ConcurrentDictionary<ulong, Session> _sessions = new ConcurrentDictionary<ulong, Session>();
        private readonly UdpClientFactory _udpClientFactory;
        private readonly ITracker _tracker;
        private const int SESSION_TimeoutSeconds = 10 * 60;
        private DateTime _lastCleanupTime = DateTime.MinValue;
        private IAccessServer AccessServer { get; }

        public string ServerId { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;

        public SessionManager(IAccessServer accessServer, UdpClientFactory udpClientFactory, ITracker tracker, string serverId)
        {
            AccessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _tracker = tracker;
            ServerId = serverId;
        }

        public Session FindSessionByClientId(Guid clientId)
        {
            var session = _sessions.FirstOrDefault(x => !x.Value.IsDisposed && x.Value.ClientId == clientId).Value;
            if (session == null)
                throw new KeyNotFoundException($"Invalid clientId! ClientId: {clientId}");

            return GetSessionById(session.SessionId);
        }

        public Session GetSessionById(ulong sessionId)
        {
            // find in session
            if (!_sessions.TryGetValue(sessionId, out Session session))
                throw new SessionException(accessUsage: null,
                    responseCode: ResponseCode.InvalidSessionId,
                    suppressedBy: SuppressType.None,
                    message: $"Invalid SessionId, SessionId: {VhLogger.FormatId(sessionId)}");

            // check session status
            if (!session.IsDisposed)
                session.UpdateStatus();

            if (session.IsDisposed)
            {
                var responseCode = session.SuppressedBy != SuppressType.None ? ResponseCode.SessionSuppressedBy : ResponseCode.SessionClosed;
                bool accessError = session.AccessController.ResponseCode != ResponseCode.Ok;
                if (accessError) responseCode = session.AccessController.ResponseCode;

                throw new SessionException(
                    accessUsage: session.AccessController.AccessUsage,
                    responseCode: responseCode,
                    suppressedBy: session.SuppressedBy,
                    message: accessError ? session.AccessController.Access.Message : "Session has been closed"
                    );
            }

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
                UserToken = helloRequest.UserToken
            };

            // validate the token
            _logger.Log(LogLevel.Trace, $"Validating the request. TokenId: {VhLogger.FormatId(clientIdentity.TokenId)}");
            var accessController = await GetValidatedAccess(clientIdentity, helloRequest.EncryptedClientId);

            // cleanup old timeout sessions
            RemoveTimeoutSessions();

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
                _logger.LogInformation($"Suppressing other session. SuppressedClientId: {VhLogger.FormatId(oldSession.ClientId)}, SuppressedSessionId: {VhLogger.FormatId(oldSession.SessionId)}");
                oldSession.SuppressedByClientId = clientIdentity.ClientId;
                oldSession.Dispose();
            }

            // create new session
            var session = new Session(clientIdentity, accessController, _udpClientFactory, timeout: SESSION_TimeoutSeconds)
            {
                SuppressedToClientId = oldSession?.ClientId
            };
            _sessions.TryAdd(session.SessionId, session);
            _tracker?.TrackEvent("Usage", "SessionCreated").GetAwaiter();
            _logger.Log(LogLevel.Information, $"New session has been created. SessionId: {VhLogger.FormatId(session.SessionId)}");

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
            RemoveTimeoutSessions(true);
            var msg = $"*** GC Collect ***, ";
            msg += $"ActiveSessionCount: {_sessions.Count(x => !x.Value.IsDisposed)}, ";
            msg += $"DisposedSessionCount: {_sessions.Count(x => x.Value.IsDisposed)}, ";
            msg += $"TotalStreamChannel: {_sessions.Sum(x => x.Value.Tunnel.StreamChannels.Length)}, ";
            msg += $"TotalDatagramChannel: {_sessions.Sum(x => x.Value.Tunnel.DatagramChannels.Length)}";
            _logger.LogInformation(msg);
        }

        private void RemoveTimeoutSessions(bool force = false)
        {
            if (!force && (DateTime.Now - _lastCleanupTime).TotalSeconds < SESSION_TimeoutSeconds)
                return;
            _lastCleanupTime = DateTime.Now;

            // update all sessions satus
            foreach (var session in _sessions.Where(x => !x.Value.IsDisposed))
                session.Value.UpdateStatus();

            // removing timeout Sessions
            _logger.Log(LogLevel.Trace, $"Removing timeout sessions...");
            var disposedSessions = _sessions.Where(x => x.Value.IsDisposed && (DateTime.Now - x.Value.DisposedTime.Value).TotalSeconds >= SESSION_TimeoutSeconds);
            foreach (var item in disposedSessions)
            {
                _sessions.Remove(item.Key, out _);
                item.Value.Dispose();
                _logger.Log(LogLevel.Information, $"Session has been removed! ClientId: {VhLogger.FormatId(item.Value.ClientId)}, SessionId: {VhLogger.FormatId(item.Value.SessionId)}");
            }

            // free server memory now
            GC.Collect();
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
                session.Dispose();
        }
    }
}