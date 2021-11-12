using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Models
{
    public class ProjectClient
    {
        public Guid ProjectClientId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ClientId { get; set; }
        public string? ClientIp { get; set; }
        public string? Country { get; set; }
        public string? UserAgent { get; set; }
        public string? ClientVersion { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual Project? Project { get; set; }

        [JsonIgnore] public virtual ICollection<Access>? Accesses { get; set; }
    }
}