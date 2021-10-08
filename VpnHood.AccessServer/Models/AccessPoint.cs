using System;

namespace VpnHood.AccessServer.Models
{
    public class AccessPoint
    {
        public Guid AccessPointId { get; set; }
        public Guid ProjectId { get; set; } //todo remove
        public string PublicIpAddress { get; set; } = default!;
        public string PrivateIpAddress { get; set; } = default!;
        public int TcpPort { get; set; }
        public int UdpPort { get; set; }
        public Guid AccessPointGroupId { get; set; }
        public Guid ServerId { get; set; }
        public bool IncludeInAccessToken { get; set; } 

        public virtual Project? Project { get; set; }
        public virtual Server? Server { get; set; }
        public virtual AccessPointGroup? AccessPointGroup { get; set; }
    }
}