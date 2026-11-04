using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

// A name this side does not know reads as Tcp: the protocol every engine and server has.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ChannelProtocol>))]
[JsonEnumFallback(ChannelProtocol.Tcp)]
public enum ChannelProtocol
{
    Quic,
    Udp,
    Tcp
}
