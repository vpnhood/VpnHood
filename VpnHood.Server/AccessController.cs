using System;
using System.Threading.Tasks;
using VpnHood.Client;
using VpnHood.Messages;

namespace VpnHood.Server
{
    public class AccessController
    {
        public long SyncSize { get; set; } = 10 * 1000000;
        private readonly IAccessServer _accessServer;
        private readonly object _syncLock = new object();
        private long _sentTrafficByteCount;
        private long _receivedTrafficByteCount;
        private bool _isSyncing = false;
        public Access Access { get; internal set; }
        public ClientIdentity ClientIdentity { get; }

        public AccessController(ClientIdentity clientIdentity, IAccessServer accessServer, Access access)
        {
            ClientIdentity = clientIdentity;
            Access = access;
            _accessServer = accessServer;
            if (access.MaxTrafficByteCount == 0)
                SyncSize = 100 * 1000000;
        }

        internal void UpdateStatusCode()
        {
            if (Access.ExpirationTime != null && Access.ExpirationTime < DateTime.Now)
                Access.StatusCode = AccessStatusCode.Expired;

            if (Access.MaxTrafficByteCount != 0 && Access.StatusCode == AccessStatusCode.Ok && 
                (Access.SentTrafficByteCount + Access.ReceivedTrafficByteCount + _sentTrafficByteCount + _receivedTrafficByteCount) > Access.MaxTrafficByteCount)
                Access.StatusCode = AccessStatusCode.TrafficOverflow;

            // set messages; Note: Access.StatusCode may come from access server
            if (Access.StatusCode == AccessStatusCode.Expired) Access.Message = "Access has been expired!";
            else if (Access.StatusCode == AccessStatusCode.TrafficOverflow) Access.Message = "Traffic has been overflowed!";
        }

        public Task AddUsage(long sentTrafficByteCount, long receivedTrafficByteCount)
        {
            lock (_syncLock)
            {
                _sentTrafficByteCount += sentTrafficByteCount;
                _receivedTrafficByteCount += receivedTrafficByteCount;
                if (_isSyncing || _sentTrafficByteCount + _receivedTrafficByteCount < SyncSize)
                    return Task.FromResult(0);
            }

            return Sync();
        }

        public async Task Sync()
        {
            AddUsageParams addUsageParam;
            lock (_syncLock)
            {
                if (_isSyncing) return;
                _isSyncing = true;

                addUsageParam = new AddUsageParams()
                {
                    ClientIdentity = ClientIdentity,
                    SentTrafficByteCount = _sentTrafficByteCount,
                    ReceivedTrafficByteCount = _receivedTrafficByteCount,
                };
            }

            try
            {
                var access = await _accessServer.AddUsage(addUsageParam);
                lock (_syncLock)
                {
                    _sentTrafficByteCount -= addUsageParam.SentTrafficByteCount;
                    _receivedTrafficByteCount -= addUsageParam.ReceivedTrafficByteCount;
                    Access = access;
                }
            }
            finally
            {
                lock (_syncLock)
                    _isSyncing = false;
            }
        }

        public AccessUsage AccessUsage => new AccessUsage
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
            _ => ResponseCode.GeneralError,
        };


    }
}