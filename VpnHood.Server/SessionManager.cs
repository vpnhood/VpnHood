using VpnHood.Messages;
using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public class SessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<ulong, Session> Sessions = new ConcurrentDictionary<ulong, Session>();
        private readonly ConcurrentDictionary<ulong, (SuppressType, DateTime)> SuppressedSessions = new ConcurrentDictionary<ulong, (SuppressType, DateTime)>();
        private readonly UdpClientFactory _udpClientFactory;
        private const int _sessionTimeoutSeconds = 60 * 15;
        private DateTime _lastCleanupTime = DateTime.MinValue;
        private ILogger Logger => Loggers.Logger.Current;
        public IAccessServer AccessServer { get; }

        public SessionManager(IAccessServer accessServer, UdpClientFactory udpClientFactory)
        {
            AccessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
        }

        public Session FindSessionById(ulong sessionId, out SuppressType suppressedBy)
        {
            // find session
            suppressedBy = SuppressType.None;
            if (Sessions.TryGetValue(sessionId, out Session session))
                return session;

            // find suppression
            if (SuppressedSessions.TryGetValue(sessionId, out (SuppressType, DateTime) value))
                suppressedBy = value.Item1;

            return null;
        }

        public async Task<Session> CreateSession(HelloRequest helloRequest, IPAddress clientIp)
        {
            // create the identity
            var clientIdentity = new ClientIdentity()
            {
                ClientId = helloRequest.ClientId,
                ClientIp = clientIp,
                TokenId = helloRequest.TokenId,
                UserToken = helloRequest.UserToken
            };
            
            // validate the token
            Logger.Log(LogLevel.Trace, $"Validating the request. TokenId: {clientIdentity.TokenId}");
            var access = await GetValidatedAccess(clientIdentity, helloRequest.EncryptedClientId);

            // cleanup old timeout sessions
            RemoveTimeoutSessions();

            // suppress other session of same client if maxClient is exceeded
            Guid? suppressedClientId = null;
            var oldSession = FindSessionByClientId(clientIdentity.ClientId);
            if (oldSession == null && access.MaxClient > 0) // no limitation if MaxClientCount is zero
            {
                var otherSessions = FindSessionsByTokenId(clientIdentity.TokenId).OrderBy(x => x.CreatedTime).ToArray();
                if (otherSessions.Length >= access.MaxClient)
                    oldSession = otherSessions[0];
            }

            if (oldSession != null)
            {
                Logger.LogInformation($"Suppressing other session. SuppressedClientId: {Util.FormatId(oldSession.ClientId)}, SuppressedSessionId: {Util.FormatId(oldSession.SessionId)}");
                suppressedClientId = oldSession.ClientId;
                Sessions.TryRemove(oldSession.SessionId, out _);
                SuppressedSessions.TryAdd(oldSession.SessionId, (clientIdentity.ClientId == oldSession.ClientId ? SuppressType.YourSelf : SuppressType.Other, DateTime.Now));
                oldSession.Dispose();
            }

            // create new session
            var session = new Session(clientIdentity, access, _udpClientFactory)
            {
                SuppressedToClientId = oldSession?.ClientId
            };
            Sessions.TryAdd(session.SessionId, session);
            Logger.Log(LogLevel.Information, $"New session has been created. SessionId: {Util.FormatId(session.SessionId)}");

            return session;
        }

        private async Task<Access> GetValidatedAccess(ClientIdentity clientIdentity, byte[] encryptedClientId)
        {
            // get access
            var access = await AccessServer.GetAccess(clientIdentity);
            if (access == null)
                throw new Exception($"Could not find the tokenId! {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            // Validate token by shared secret
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = access.Secret;
            aes.IV = new byte[access.Secret.Length];
            aes.Padding = PaddingMode.None;
            using var cryptor = aes.CreateEncryptor();
            var ecid = cryptor.TransformFinalBlock(clientIdentity.ClientId.ToByteArray(), 0, clientIdentity.ClientId.ToByteArray().Length);
            if (!Enumerable.SequenceEqual(ecid, encryptedClientId))
                throw new Exception($"The request does not have a valid signature for requested token! {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            // check access
            if (access.StatusCode!= AccessStatusCode.Ok)
                throw new AccessException(access);

            return access;
        }

        private bool CheckSessionTimeout(DateTime time) => (DateTime.Now - time).TotalSeconds < _sessionTimeoutSeconds;

        private void RemoveTimeoutSessions()
        {
            if ((DateTime.Now - _lastCleanupTime).TotalSeconds < _sessionTimeoutSeconds)
                return;
            _lastCleanupTime = DateTime.Now;

            // removing timeout Sessions
            Logger.Log(LogLevel.Trace, $"Removing timeout sessions...");
            foreach (var item in Sessions.ToArray())
                if (CheckSessionTimeout(item.Value.Tunnel.LastActivityTime))
                {
                    Sessions.Remove(item.Key, out _);
                    item.Value.Dispose();
                }

            // removing timeout SuppressedSessions
            foreach (var item in SuppressedSessions.ToArray())
                if ((DateTime.Now - item.Value.Item2).TotalSeconds < _sessionTimeoutSeconds)
                    SuppressedSessions.TryRemove(item.Key, out _);
        }

        public Session FindSessionByClientId(Guid cliendId)
        {
            return Sessions.Values.FirstOrDefault(x => x.ClientId == cliendId);
        }

        public Session[] FindSessionsByTokenId(Guid tokenId)
        {
            return Sessions.Values.Where(x => x.ClientIdentity.TokenId == tokenId).ToArray();
        }

        public void Dispose()
        {
            foreach (var session in Sessions.Values)
                session.Dispose();
        }
    }
}