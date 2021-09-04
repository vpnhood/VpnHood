using System;

namespace VpnHood.AccessServer.DTOs
{
    public class EndPointGroupCreateParams
    {
        public string? AccessTokenGroupName { get; set; }
        public Guid? CertificateId { get; set; }
        public bool MakeDefault { get; set; }
    }
}