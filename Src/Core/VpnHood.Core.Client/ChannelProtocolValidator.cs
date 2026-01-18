using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client;

internal static class ChannelProtocolValidator
{
    public static ChannelProtocol[] GetChannelProtocols(HelloResponse helloResponse)
    {
        var channelProtocols = new List<ChannelProtocol> { ChannelProtocol.Tcp };
        if (helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 })
            channelProtocols.Add(ChannelProtocol.Udp);

        return channelProtocols.ToArray();
    }

    public static ChannelProtocol Validate(ChannelProtocol value, SessionInfo? sessionInfo)
    {
        if (value == ChannelProtocol.Quic)
            throw new NotSupportedException("QUIC is not supported");

        if (sessionInfo == null)
            return value;

        if (value == ChannelProtocol.Udp && !sessionInfo.IsUdpChannelSupported)
            return ChannelProtocol.Tcp;

        return value;
    }
}