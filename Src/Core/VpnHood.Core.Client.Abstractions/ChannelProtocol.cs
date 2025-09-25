using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChannelProtocol
{
    Udp,
    Tcp,
    TcpProxyAndUdp,
    TcpProxy,
    TcpProxyAndDropQuic
}