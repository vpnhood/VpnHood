using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointGroupCreateParams
    {
        public string? AccessPointGroupName { get; set; }
        public Guid? CertificateId { get; set; }
        public bool MakeDefault { get; set; }
    }
}