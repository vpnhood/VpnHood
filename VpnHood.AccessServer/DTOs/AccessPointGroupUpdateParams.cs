using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointGroupUpdateParams
    {
        public Wise<string?>? AccessPointGroupName {get;set;}
        public Wise<Guid>? CertificateId { get;set;}
    }
}