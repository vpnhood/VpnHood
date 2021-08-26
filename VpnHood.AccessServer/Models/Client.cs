using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public partial class Client
    {
        public Guid ClientKeyId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ClientId { get; set; }
        public string? ClientIp { get; set; }
        public string? UserAgent { get; set; }
        public string? ClientVersion { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual Project? Project { get; set; }

        [JsonIgnore]
        public virtual ICollection<AccessUsage>? AccessUsages { get; set; }
    }
}
