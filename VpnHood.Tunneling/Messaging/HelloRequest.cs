using System;
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging
{
    public class HelloRequest : SessionRequest
    {
        [JsonConstructor]
        public HelloRequest(Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
            : base(tokenId, clientInfo, encryptedClientId)
        {
        }

        public bool UseUdpChannel { get; set; } //todo use global udp
    }
}