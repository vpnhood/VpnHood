using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Tunneling.Messaging;

public class StreamProxyChannelRequest()
    : RequestBase(Messaging.RequestCode.ProxyChannel)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }
}