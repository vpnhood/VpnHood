using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Tunneling.Messaging;

public class StreamProxyChannelRequest()
    : RequestBase(Messaging.RequestCode.ProxyChannel)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }

    [Obsolete("Will be removed after 688. Required for server compatibility smaller than 688.")]
    public byte[] CipherKey { get; init; } = [];

    [Obsolete("Will be removed after 688. Required for server compatibility smaller than 688.")]
    public long CipherLength { get; init; } = 0;
}