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
        public IClientStore TokenStore { get; }

        public SessionManager(IClientStore tokenStore, UdpClientFactory udpClientFactory)
        {
            TokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "<Pending>")]
        public async Task<Session> CreateSession(HelloRequest helloRequest, IPEndPoint remoteEndPoint)
        {
            Logger.Log(LogLevel.Trace, $"Validating the request. TokenId: {helloRequest.TokenId}");

            // validate the token
            var clientInfo = await GetValidatedClientInfo(helloRequest, remoteEndPoint.Address);
            var clientUsage = clientInfo.ClientUsage;
            var tokenSettings = clientInfo.TokenSettings;

            // cleanup old timeout sessions
            RemoveTimeoutSessions();

            // suppress other session of same client
            Guid? suppressedClientId = null;
            var oldSession = FindSessionByClientId(helloRequest.ClientId);
            if (oldSession == null && tokenSettings.MaxClientCount > 0) // no limitation if MaxClientCount is zero
            {
                var otherSessions = FindSessionsByTokenId(helloRequest.TokenId).OrderBy(x => x.CreatedTime).ToArray();
                if (otherSessions.Length >= tokenSettings.MaxClientCount)
                    oldSession = otherSessions[0];
            }

            if (oldSession != null)
            {
                Logger.LogInformation($"Suppressing other session. SuppressedClientId: {Util.FormatId(oldSession.ClientId)}, SuppressedSessionId: {Util.FormatId(oldSession.SessionId)}");
                suppressedClientId = oldSession.ClientId;
                Sessions.TryRemove(oldSession.SessionId, out _);
                SuppressedSessions.TryAdd(oldSession.SessionId, (helloRequest.ClientId == oldSession.ClientId ? SuppressType.YourSelf : SuppressType.Other, DateTime.Now));
                oldSession.Dispose();
            }

            // create new session
            var session = new Session(clientInfo, helloRequest.ClientId, _udpClientFactory)
            {
                SuppressedToClientId = oldSession?.ClientId
            };
            Sessions.TryAdd(session.SessionId, session);
            Logger.Log(LogLevel.Information, $"New session has been created. SessionId: {Util.FormatId(session.SessionId)}");

            return session;
        }

        private async Task<ClientInfo> GetValidatedClientInfo(HelloRequest helloRequest, IPAddress clientIp)
        {
            // find tokenId in store
            var clientIdentiy = new ClientIdentity()
            {
                TokenId = helloRequest.TokenId,
                ClientId = helloRequest.ClientId,
                ClientIp = clientIp
            };
            var clientInfo = await TokenStore.GetClientInfo(clientIdentiy, true);
            if (clientInfo == null)
                throw new Exception($"Could not find the tokenId! {helloRequest.TokenId}, ClientId: {helloRequest.ClientId}");

            // Validate token by shared secret
            var token = clientInfo.Token;
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = token.Secret;
            aes.IV = new byte[token.Secret.Length];
            aes.Padding = PaddingMode.None;
            using var cryptor = aes.CreateEncryptor();
            var encryptedClientId = cryptor.TransformFinalBlock(helloRequest.ClientId.ToByteArray(), 0, helloRequest.ClientId.ToByteArray().Length);
            if (!Enumerable.SequenceEqual(encryptedClientId, helloRequest.EncryptedClientId))
                throw new Exception($"The request does not have a valid signature for requested token! {helloRequest.TokenId}, ClientId: {helloRequest.ClientId}");

            return clientInfo;
        }

        private bool CheckSessionTimeout(DateTime time) => (DateTime.Now - time).TotalSeconds < _sessionTimeoutSeconds;

        private void RemoveTimeoutSessions()
        {
            if ((DateTime.Now - _lastCleanupTime).TotalSeconds < _sessionTimeoutSeconds)
                return;
            _lastCleanupTime = DateTime.Now;

            Logger.Log(LogLevel.Trace, $"Removing timeout sessions...");
            // removing timeout Sessions
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
            return Sessions.Values.Where(x => x.Token.TokenId == tokenId).ToArray();
        }

        public void Dispose()
        {
            foreach (var session in Sessions.Values)
                session.Dispose();
        }
    }
}