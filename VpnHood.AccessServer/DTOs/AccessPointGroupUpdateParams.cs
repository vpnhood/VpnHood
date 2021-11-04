using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointGroupUpdateParams
    {
        public Patch<string?>? AccessPointGroupName {get;set;}
        public Patch<Guid>? CertificateId { get;set;}
    }
}