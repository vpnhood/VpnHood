﻿using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling.Messages;

namespace VpnHood.Server
{
    public class AccessController
    {
        public long SyncSize { get; set; } = 100 * 1000000; //100 MB
        private readonly object _syncLock = new();
        private long _sentTrafficByteCount;
        private long _receivedTrafficByteCount;
        private bool _isSyncing = false;
        
        private IAccessServer AccessServer { get; }
        public ClientIdentity ClientIdentity { get; }
        public IPEndPoint ServerEndPoint { get; }
        public Access Access { get; private set; }

        public static async Task<AccessController> Create(IAccessServer accessServer, ClientIdentity clientIdentity, IPEndPoint serverEndPoint, byte[] encryptedClientId)
        {
            AccessController ret = new(accessServer, clientIdentity, serverEndPoint);
            await ret.Init(encryptedClientId);
            return ret;
        }

        public AccessController(IAccessServer accessServer, ClientIdentity clientIdentity, IPEndPoint serverEndPoint)
        {
            AccessServer = accessServer;
            ClientIdentity = clientIdentity;
            ServerEndPoint = serverEndPoint;
        }

        private async Task Init(byte[] encryptedClientId)
        {
            // get access
            var access = await AccessServer.GetAccess(new AccessParams { ClientIdentity = ClientIdentity, ServerEndPoint = ServerEndPoint });
            if (access == null)
                throw new Exception($"Could not find the tokenId! {VhLogger.FormatId(ClientIdentity.TokenId)}, ClientId: {VhLogger.FormatId(ClientIdentity.ClientId)}");

            // Validate token by shared secret
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = access.Secret;
            aes.IV = new byte[access.Secret.Length];
            aes.Padding = PaddingMode.None;
            using var cryptor = aes.CreateEncryptor();
            var ecid = cryptor.TransformFinalBlock(ClientIdentity.ClientId.ToByteArray(), 0, ClientIdentity.ClientId.ToByteArray().Length);
            if (!Enumerable.SequenceEqual(ecid, encryptedClientId))
                throw new Exception($"The request does not have a valid signature for requested token! {VhLogger.FormatId(ClientIdentity.TokenId)}, ClientId: {VhLogger.FormatId(ClientIdentity.ClientId)}");

            Access = access; // update access
            UpdateStatusCode();

            // check access
            if (Access.StatusCode != AccessStatusCode.Ok)
                throw new SessionException(
                    accessUsage: AccessUsage,
                    responseCode: ResponseCode,
                    suppressedBy: SuppressType.None,
                    redirectServerEndPint: Access.RedirectServerEndPoint,
                    message: Access.Message
                    );
        }

        public void UpdateStatusCode()
        {
            if (Access.ExpirationTime != null && Access.ExpirationTime < DateTime.Now)
                Access.StatusCode = AccessStatusCode.Expired;

            else if (Access.MaxTrafficByteCount != 0 && Access.StatusCode == AccessStatusCode.Ok &&
                (Access.SentTrafficByteCount + Access.ReceivedTrafficByteCount + _sentTrafficByteCount + _receivedTrafficByteCount) > Access.MaxTrafficByteCount)
                Access.StatusCode = AccessStatusCode.TrafficOverflow;

            // set messages; Note: Access.StatusCode may come from access server
            if (Access.StatusCode == AccessStatusCode.Expired) Access.Message = "Access has been expired!";
            else if (Access.StatusCode == AccessStatusCode.TrafficOverflow) Access.Message = "Traffic has been overflowed!";
        }

        public Task AddUsage(ClientIdentity clientIdentity, long sentTrafficByteCount, long receivedTrafficByteCount)
        {
            lock (_syncLock)
            {
                _sentTrafficByteCount += sentTrafficByteCount;
                _receivedTrafficByteCount += receivedTrafficByteCount;
                if (_isSyncing || _sentTrafficByteCount + _receivedTrafficByteCount < SyncSize)
                    return Task.FromResult(0);
            }

            return Sync(clientIdentity);
        }

        public async Task Sync(ClientIdentity clientIdentity)
        {
            UsageParams usageParam;
            lock (_syncLock)
            {
                if (_isSyncing) return;
                _isSyncing = true;

                usageParam = new UsageParams()
                {
                    AccessId = Access.AccessId,
                    ClientIdentity = clientIdentity,
                    SentTrafficByteCount = _sentTrafficByteCount,
                    ReceivedTrafficByteCount = _receivedTrafficByteCount,
                };
            }

            try
            {
                var access = await AccessServer.AddUsage(usageParam);
                lock (_syncLock)
                {
                    _sentTrafficByteCount -= usageParam.SentTrafficByteCount;
                    _receivedTrafficByteCount -= usageParam.ReceivedTrafficByteCount;
                    Access = access;
                }
            }
            finally
            {
                lock (_syncLock)
                    _isSyncing = false;
            }
        }

        public AccessUsage AccessUsage => new()
        {
            ExpirationTime = Access.ExpirationTime,
            MaxTrafficByteCount = Access.MaxTrafficByteCount,
            ReceivedByteCount = Access.ReceivedTrafficByteCount,
            SentByteCount = Access.SentTrafficByteCount,
        };

        public ResponseCode ResponseCode => Access.StatusCode switch
        {
            AccessStatusCode.Ok => ResponseCode.Ok,
            AccessStatusCode.Expired => ResponseCode.AccessExpired,
            AccessStatusCode.TrafficOverflow => ResponseCode.AccessTrafficOverflow,
            AccessStatusCode.RedirectServer => ResponseCode.RedirectServer,
            _ => ResponseCode.GeneralError,
        };
    }
}