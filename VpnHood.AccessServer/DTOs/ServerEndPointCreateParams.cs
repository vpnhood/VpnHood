using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerEndPointCreateParams
    {
        public Guid? AccessTokenGroupId { get; set; }
        public string? SubjectName { get; set; }
        public byte[]? CertificateRawData { get; set; }
        public string? CertificatePassword { get; set; }
        public bool MakeDefault { get; set; }
        
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? PrivateEndPoint { get; set; }
    }
}