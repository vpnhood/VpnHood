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
        public DateTime? ConfigureTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public string? Description { get; set; }
        public Guid AuthorizationCode { get; set; }
        public byte[] Secret { get; set; } = default!;
        public Guid? AccessPointGroupId { get; set; } //AutoUpdateAccessPoint

        public virtual Project? Project { get; set; }
        public virtual AccessPointGroup? AccessPointGroup { get; set; }

        [JsonIgnore] public virtual ICollection<AccessLog>? AccessUsageLogs { get; set; }
        [JsonIgnore] public virtual ICollection<Session>? Sessions { get; set; }
        [JsonIgnore] public virtual ICollection<AccessPoint>? AccessPoints { get; set; }

    }
}