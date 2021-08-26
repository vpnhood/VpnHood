using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Models
{
    public partial class Session
    {
        public uint SessionId { get; set; }
        public Guid AccessUsageId { get; set; }
        public Guid ClientKeyId { get; set; }
        public string ClientVersion { get; set; } = null!;
        public string? ClientIp { get; set; }
        public byte[]? SessionKey { get; set; } = null!;
        public Guid ServerId { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public DateTime AccessedTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public SessionSuppressType SuppressedBy { get; set; }
        public SessionSuppressType SuppressedTo { get; set; }
        public SessionErrorCode ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public virtual Server? Server { get; set; }
        public virtual Client? Client { get; set; }
        public virtual AccessUsage? AccessUsage { get; set; }

        [JsonIgnore]
        public virtual ICollection<AccessUsageLog>? AccessUsageLogs { get; set; }
    }
}
