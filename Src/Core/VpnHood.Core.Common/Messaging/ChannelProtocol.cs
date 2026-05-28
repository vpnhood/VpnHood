using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChannelProtocol
{
    Quic,
    Udp,
    Tcp
}