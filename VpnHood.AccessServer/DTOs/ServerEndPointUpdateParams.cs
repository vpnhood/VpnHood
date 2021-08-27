using System;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerEndPointUpdateParams
    {
        public Wise<Guid>? AccessTokenGroupId { get; set; }
        public Wise<byte[]>? CertificateRawData { get; set; }
        public Wise<string>? CertificatePassword { get; set; }
        public Wise<bool>? MakeDefault { get; set; }
    }
}