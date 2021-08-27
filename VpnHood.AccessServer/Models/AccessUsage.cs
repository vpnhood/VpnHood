using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class AccessUsage
    {
        public Guid AccessUsageId { get; set; }
        public Guid AccessTokenId { get; set; }
        public Guid? ProjectClientId { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }
        public DateTime CreatedTime { get; set; } //todo what is this?
        public DateTime ModifiedTime { get; set; }
        public DateTime? EndTime { get; set; }

        public virtual AccessToken? AccessToken { get; set; }
        public virtual ProjectClient? Client { get; set; }

        [JsonIgnore] public virtual ICollection<AccessUsageLog>? AccessUsageLogs { get; set; }
    }
}