using System;

namespace VpnHood.AccessServer.Models
{
    public class AccessPointGroup
    {
        public Guid AccessPointGroupId { get; set; }
        public string? AccessPointGroupName { get; set; }
        public Guid ProjectId { get; set; }
        public Guid CertificateId { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedTime { get; set; }

        public virtual Project? Project { get; set; }
        public virtual Certificate? Certificate { get; set; }
    }
}