using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerEndPointCreateParams
    {
        public Guid? AccessTokenGroupId { get; set; }
        
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? PrivateEndPoint { get; set; }

        public bool MakeDefault { get; set; }
       
    }
}