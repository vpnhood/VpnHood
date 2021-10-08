using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class Server
    {
        public Guid ProjectId { get; set; }
        public Guid ServerId { get; set; }
        public string? Version { get; set; }
        public string? ServerName { get; set; }
        public string? EnvironmentVersion { get; set; }
        public string? OsInfo { get; set; }
        public string? MachineName { get; set; }
        public long TotalMemory { get; set; }
        public IPAddress? PrivateIpV4 { get; set; }
        public IPAddress? PrivateIpV6 { get; set; }
        public IPAddress? PublicIpV4 { get; set; }
        public IPAddress? PublicIpV6 { get; set; }
        public DateTime? SubscribeTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public string? Description { get; set; }
        public Guid AuthorizationCode { get; set; }
        public byte[] Secret { get; set; } = default!;
        public bool AutoCreateAccessPoint { get; set; }

        public virtual Project? Project { get; set; }

        [JsonIgnore] public virtual ICollection<AccessLog>? AccessUsageLogs { get; set; }
        [JsonIgnore] public virtual ICollection<Session>? Sessions { get; set; }
        [JsonIgnore] public virtual ICollection<AccessPoint>? AccessPoints { get; set; }

    }
}