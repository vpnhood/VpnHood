using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ClientIdentity
    {
        public Guid TokenId { get; set; }
        public string UserToken { get; set; }
        public Guid ClientId { get; set; }
        
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress ClientIp { get; set; }
        public string ClientVersion { get; set; }
        public string UserAgent { get; set; }

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint ServerEndPoint { get; set; }
    }
}
