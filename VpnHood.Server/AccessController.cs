using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VpnHood.Client;
using VpnHood.Messages;

namespace VpnHood.Server
{
    public class AccessController
    {
        private const long CACHE_SIZE = 50 * 1000000;
        private readonly IAccessServer _accessServer;
        private readonly object _objectLock = new object();
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
        }

        internal void UpdateStatusCode()
        {
            if (Access.ExpirationTime != null && Access.ExpirationTime < DateTime.Now)
            {
                Access.StatusCode = AccessStatusCode.Expired;
                Access.Message = "Access has been expired!";
            }

            if (Access.MaxTrafficByteCount != 0 && Access.StatusCode == AccessStatusCode.Ok && (Access.SentTrafficByteCount + Access.ReceivedTrafficByteCount) > Access.MaxTrafficByteCount)
            {
                Access.StatusCode = AccessStatusCode.TrafficOverflow;
                Access.Message = "Traffic has been overflowed!";
            }
        }

        public Task AddUsage(long sentTrafficByteCount, long receivedTrafficByteCount)
        {

            lock (_objectLock)
            {
                _sentTrafficByteCount += sentTrafficByteCount;
                _receivedTrafficByteCount += receivedTrafficByteCount;
                Access.SentTrafficByteCount += sentTrafficByteCount;
                Access.ReceivedTrafficByteCount += receivedTrafficByteCount;

                if (!_isSyncing && _sentTrafficByteCount + _receivedTrafficByteCount > CACHE_SIZE)
                {
                    _isSyncing = true;
                    var pendingSent = _sentTrafficByteCount;
                    var pendingReceived = _receivedTrafficByteCount;

                    _accessServer.AddUsage(ClientIdentity, pendingSent, pendingReceived).
                        ContinueWith((task) =>
                        {
                            lock (_objectLock)
                            {
                                _sentTrafficByteCount -= pendingSent;
                                _receivedTrafficByteCount -= pendingReceived;
                                _isSyncing = false;
                            }
                        });
                }
            }

            return Task.FromResult(0);
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