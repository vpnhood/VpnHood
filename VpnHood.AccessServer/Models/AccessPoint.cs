using System;

namespace VpnHood.AccessServer.Models
{
    public class AccessPoint
    {
        public Guid AccessPointId { get; set; }
        public Guid ProjectId { get; set; }
        public string PublicEndPoint { get; set; } = null!;
        public string? PrivateEndPoint { get; set; }
        public Guid AccessPointGroupId { get; set; }
        public Guid? ServerId { get; set; }
        public bool IsDefault { get; set; }

        public virtual Project? Project { get; set; }
        public virtual Server? Server { get; set; }
        public virtual AccessPointGroup? AccessPointGroup { get; set; }
    }
}