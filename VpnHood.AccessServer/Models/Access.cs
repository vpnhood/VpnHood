using System;

namespace VpnHood.AccessServer.Models
{
    public class Access
    {
        public Guid AccessId { get; set; }
        public Guid AccessTokenId { get; set; }
        public Guid? ProjectClientId { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public DateTime? EndTime { get; set; }

        public virtual AccessToken? AccessToken { get; set; }
        public virtual ProjectClient? ProjectClient { get; set; }
    }
}