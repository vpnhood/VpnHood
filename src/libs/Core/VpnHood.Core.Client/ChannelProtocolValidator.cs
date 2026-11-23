using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client;

internal static class ChannelProtocolValidator
{
    // a server's UDP port is of use from its protocol on: an older server's UDP has neither the server-reply bit nor the
    // freshness mark
    public static bool IsUdpChannelOffered(HelloResponse helloResponse) =>
        helloResponse is { UdpPort: > 0, ProtocolVersion: >= UdpDatagram.MinProtocolVersion };

    // what the session can carry its packets on: TCP always, UDP and QUIC where the session has them
    public static IReadOnlyList<ChannelProtocol> GetChannelProtocols(bool isUdpChannelSupported,
        bool isQuicChannelSupported)
    {
        var channelProtocols = new List<ChannelProtocol> { ChannelProtocol.Tcp };
        if (isUdpChannelSupported)
            channelProtocols.Add(ChannelProtocol.Udp);
        if (isQuicChannelSupported)
            channelProtocols.Add(ChannelProtocol.Quic);

        return channelProtocols;
    }

    public static ChannelProtocol Validate(ChannelProtocol value, SessionInfo sessionInfo)
    {
        if (value == ChannelProtocol.Quic && !sessionInfo.IsQuicChannelSupported) {
            VhLogger.Instance.LogWarning("The session has no QUIC channel, so it takes TCP.");
            value = ChannelProtocol.Tcp;
        }

        if (value == ChannelProtocol.Udp && !sessionInfo.IsUdpChannelSupported) {
            VhLogger.Instance.LogWarning("The session has no UDP channel, so it takes TCP.");
            return ChannelProtocol.Tcp;
        }

        return value;
    }
}