using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public partial class AccessUsage
    {
        public Guid AccessUsageId { get; set; }
        public Guid AccessTokenId { get; set; }
        public Guid? ClientKeyId { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }
        public DateTime ConnectTime { get; set; }
        public DateTime ModifiedTime { get; set; }

        public virtual AccessToken? AccessToken { get; set; }
        public virtual Client? Client { get; set; }

        [JsonIgnore]
        public virtual ICollection<AccessUsageLog>? AccessUsageLogs { get; set; }

    }
}
