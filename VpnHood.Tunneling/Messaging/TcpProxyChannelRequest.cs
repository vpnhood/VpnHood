using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messaging;

[method: JsonConstructor]
public class StreamProxyChannelRequest(
    string requestId, ulong sessionId, byte[] sessionKey,
    IPEndPoint destinationEndPoint, byte[] cipherKey, long cipherLength)
    : RequestBase(Messaging.RequestCode.StreamProxyChannel, requestId, sessionId, sessionKey)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint DestinationEndPoint { get; set; } = destinationEndPoint;

    public byte[] CipherKey { get; set; } = cipherKey;
    public long CipherLength { get; set; } = cipherLength;
}