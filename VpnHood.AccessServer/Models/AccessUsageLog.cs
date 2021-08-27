using System;

namespace VpnHood.AccessServer.Models
{
    public class AccessUsageLog
    {
        public long AccessUsageLogId { get; set; }
        public long SessionId { get; set; }
        public Guid ServerId { get; set; }
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual Server? Server { get; set; }
        public virtual Session? Session { get; set; }
    }
}