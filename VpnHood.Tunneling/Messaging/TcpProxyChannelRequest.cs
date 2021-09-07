using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messaging
{
    public class TcpProxyChannelRequest : RequestBase
    {
        [JsonConstructor]
        public TcpProxyChannelRequest(uint sessionId, byte[] sessionKey, IPEndPoint destinationEndPoint,
            byte[] cipherKey, long cipherLength)
            : base(sessionId, sessionKey)
        {
            DestinationEndPoint = destinationEndPoint;
            CipherKey = cipherKey;
            CipherLength = cipherLength;
        }

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint DestinationEndPoint { get; set; }

        public byte[] CipherKey { get; set; }
        public long CipherLength { get; set; }
    }
}