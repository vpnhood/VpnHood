using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointCreateParams
    {
        public AccessPointCreateParams(IPAddress ipAddress)
        {
            IpAddress = ipAddress;
        }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress IpAddress { get; set; }
        public int TcpPort { get; set; } = 443;
        public int UdpPort { get; set; } = 0;
        public Guid AccessPointGroupId { get; set; } //todo: move to constructor
        public AccessPointMode AccessPointMode { get; set; }
        public bool IsListen { get; set; } = true;
    }
}