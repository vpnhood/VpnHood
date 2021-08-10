using System;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class ServerEndPointCreateParams
    {
        public Guid? AccessTokenGroupId { get; set; }
        public string SubjectName { get; set; }
        public byte[] CertificateRawData { get; set; }
        public string CertificatePassword { get; set; }
        public bool MakeDefault { get; set; }
    }
}
