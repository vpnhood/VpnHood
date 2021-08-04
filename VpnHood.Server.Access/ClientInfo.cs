using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class ClientInfo
    {
        public string UserToken { get; set; }
        public Guid ClientId { get; set; }
        
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress ClientIp { get; set; }
        public string ClientVersion { get; set; }
        public string UserAgent { get; set; }

        public override string ToString() 
            => $"{nameof(ClientId)}={ClientId}, {nameof(ClientIp)}={ClientIp}";
    }
}
