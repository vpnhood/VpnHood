using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class Client
    {
        public Guid ClientId { get; set; }
        public string UserAgent { get; set; }
        public string ClientVersion { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual ICollection<AccessUsage> AccessUsages { get; set; } = new HashSet<AccessUsage>();
        public virtual ICollection<AccessUsageLog> AccessUsageLogs { get; set; } = new HashSet<AccessUsageLog>();
    }
}
