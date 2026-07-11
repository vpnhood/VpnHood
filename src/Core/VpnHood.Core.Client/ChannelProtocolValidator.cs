using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client;

internal static class ChannelProtocolValidator
{
    public static ChannelProtocol[] GetChannelProtocols(HelloResponse helloResponse, bool isQuicSupported)
    {
        var channelProtocols = new List<ChannelProtocol> { ChannelProtocol.Tcp };
        if (helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 })
            channelProtocols.Add(ChannelProtocol.Udp);
        if (helloResponse is { QuicPort: > 0 } && isQuicSupported)
            channelProtocols.Add(ChannelProtocol.Quic);

        return channelProtocols.ToArray();
    }

    public static ChannelProtocol Validate(ChannelProtocol value, SessionInfo sessionInfo)
    {
        if (value == ChannelProtocol.Quic && !sessionInfo.IsQuicChannelSupported) {
            VhLogger.Instance.LogWarning("Quic protocol is not supported, fallback to Tcp");
            value = ChannelProtocol.Tcp;
        }

        if (value == ChannelProtocol.Udp && !sessionInfo.IsUdpChannelSupported) {
            VhLogger.Instance.LogWarning("Udp protocol is not supported, fallback to Tcp");
            return ChannelProtocol.Tcp;
        }

        return value;
    }
}