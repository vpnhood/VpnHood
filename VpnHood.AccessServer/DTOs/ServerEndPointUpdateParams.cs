using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerEndPointUpdateParams
    {
        public Wise<Guid>? AccessTokenGroupId { get; set; }
        public Wise<byte[]>? CertificateRawData { get; set; }
        public Wise<string>? CertificatePassword { get; set; }
        public Wise<bool>? MakeDefault { get; set; }
        public Wise<string?>? PrivateEndPoint { get; set; }

    }
}