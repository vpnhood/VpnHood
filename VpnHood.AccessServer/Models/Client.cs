using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class Client
    {
        public Client()
        {
            AccessUsages = new HashSet<AccessUsage>();
            UsageLogs = new HashSet<UsageLog>();
        }

        public Guid ClientId { get; set; }
        public string UserAgent { get; set; }
        public string ClientVersion { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual ICollection<AccessUsage> AccessUsages { get; set; }
        public virtual ICollection<UsageLog> UsageLogs { get; set; }
    }
}
