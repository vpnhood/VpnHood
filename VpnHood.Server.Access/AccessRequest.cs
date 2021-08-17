using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server
{
    public class AccessRequest
    {
        public Guid TokenId { get; set; }

        public ClientInfo ClientInfo { get; set; }

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint RequestEndPoint { get; set; }

        public AccessRequest(Guid tokenId, ClientInfo clientInfo, IPEndPoint requestEndPoint)
        {
            TokenId = tokenId;
            ClientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));
            RequestEndPoint = requestEndPoint ?? throw new ArgumentNullException(nameof(requestEndPoint));
        }
    }
}
