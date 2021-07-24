using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class AccessUsage
    {
        public Guid AccessTokenId { get; set; }
        public string ClientIp { get; set; }
        public long CycleSentTraffic { get; set; }
        public long CycleReceivedTraffic { get; set; }
        public long TotalSentTraffic { get; set; }
        public long TotalReceivedTraffic { get; set; }

        public virtual AccessToken AccessToken { get; set; }
    }
}
