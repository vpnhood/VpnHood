using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Messaging
{
    public class SessionRequestEx : SessionRequest
    {
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint HostEndPoint { get; set; }

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress? ClientIp { get; set; }

        [JsonConstructor]
        public SessionRequestEx(Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId, IPEndPoint hostEndPoint)
            : base(tokenId, clientInfo, encryptedClientId)
        {
            HostEndPoint = hostEndPoint;
        }

        public SessionRequestEx(SessionRequest obj, IPEndPoint hostEndPoint)
            : base(obj)
        {
            HostEndPoint = hostEndPoint;
        }
    }
}
