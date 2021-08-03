using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class Server
    {
        public Guid ProjectId { get; set; }
        public Guid ServerId { get; set; }
        public string ServerName { get; set; }
        public string Version { get; set; }
        public string EnvironmentVersion { get; set; }
        public string OsVersion { get; set; }
        public string MachineName { get; set; }
        public long TotalMemory { get; set; }
        public IPAddress LocalIp { get; set; }
        public IPAddress PublicIp { get; set; }
        public DateTime SubscribeTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public string Description { get; set; }

        public virtual Project Account { get; set; }
        
        [JsonIgnore]
        public virtual ICollection<ServerEndPoint> ServerEndPoints { get; set; } = new HashSet<ServerEndPoint>();
        
        [JsonIgnore]
        public virtual ICollection<AccessUsageLog> AccessUsageLogs { get; set; } = new HashSet<AccessUsageLog>();
        
        [JsonIgnore]
        public virtual ICollection<ServerStatusLog> ServerStatus { get; set; } = new HashSet<ServerStatusLog>();
    }
}
