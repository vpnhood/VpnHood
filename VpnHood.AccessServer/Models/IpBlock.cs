using System;

namespace VpnHood.AccessServer.Models
{
    public class IpBlock
    {
        public Guid ProjectId { get; set; }
        public string Ip { get; set; } = default!;
        public DateTime? BlockedTime { get; set; }
        public string? Description { get; set; }
    }
}