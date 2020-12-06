using System;

namespace VpnHood.Tunneling.Messages
{
    public class AccessUsage
    {
        public long SentByteCount { get; set; }
        public long ReceivedByteCount { get; set; }
        public long MaxTrafficByteCount { get; set; }
        public DateTime? ExpirationTime { get; set; }
    }
}
