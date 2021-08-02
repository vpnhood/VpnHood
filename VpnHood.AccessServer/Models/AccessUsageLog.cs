﻿using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class AccessUsageLog
    {
        public long AccessUsageLogId { get; set; }
        public Guid AccessUsageId { get; set; }
        public Guid ClientKeyId { get; set; }
        public string ClientIp { get; set; }
        public string ClientVersion { get; set; }
        public Guid ServerId { get; set; }
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual Server Server { get; set; }
        public virtual Client Client { get; set; }
        public virtual AccessUsage AccessUsage { get; set; }
    }
}
