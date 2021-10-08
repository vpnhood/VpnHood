using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointCreateParams
    {
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress PublicIpAddress { get; set; } = default!;

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress? PrivateIpAddress { get; set; } = default!;

        public int TcpPort { get; set; } = 443;
        public int UdpPort { get; set; } = 0;
        public Guid AccessPointGroupId { get; set; }
        public bool IncludeInAccessToken { get; set; }
      
    }
}