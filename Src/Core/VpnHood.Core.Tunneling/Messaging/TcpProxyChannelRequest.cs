using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Tunneling.Messaging;

public class StreamProxyChannelRequest()
    : RequestBase(Messaging.RequestCode.ProxyChannel)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }
    public required ReadOnlyMemory<byte> InitContents { get; set; } // supported in protocol v12+
}