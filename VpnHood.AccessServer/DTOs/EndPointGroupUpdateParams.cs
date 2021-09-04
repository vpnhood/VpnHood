using System;

namespace VpnHood.AccessServer.DTOs
{
    public class EndPointGroupUpdateParams
    {
        public Wise<string?>? AccessTokenGroupName {get;set;}
        public Wise<Guid>? CertificateId { get;set;}
        public Wise<bool>? MakeDefault { get;set;}
    }
}