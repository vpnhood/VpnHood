using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointCreateParams
    {
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint PublicEndPoint { get; set; } = default!;

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? PrivateEndPoint { get; set; }
        public Guid? AccessPointGroupId { get; set; }

        public bool MakeDefault { get; set; }
       
    }
}