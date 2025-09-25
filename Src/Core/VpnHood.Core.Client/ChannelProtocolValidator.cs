using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client;

internal static class ChannelProtocolValidator
{
    public static ChannelProtocol[] GetChannelProtocols(HelloResponse helloResponse, bool isTcpProxySupported)
    {
        var channelProtocols = new List<ChannelProtocol>();

        // if the server does support tcp packet (adapter on server)
        if (helloResponse.IsTcpPacketSupported) {
            if (helloResponse.UdpPort > 0)
                channelProtocols.Add(ChannelProtocol.Udp);
            channelProtocols.Add(ChannelProtocol.Tcp);
        }

        // if server does support tcp proxy (disabled on server for performance)
        if (helloResponse.IsTcpProxySupported && isTcpProxySupported) {
            if (helloResponse.UdpPort > 0)
                channelProtocols.Add(ChannelProtocol.TcpProxyAndUdp);
            channelProtocols.Add(ChannelProtocol.TcpProxy);
            channelProtocols.Add(ChannelProtocol.TcpProxyAndDropQuic);
        }

        return channelProtocols.ToArray();
    }

    public static ChannelProtocol Validate(ChannelProtocol value, SessionInfo? sessionInfo, bool isTcpProxySupported)
    {
        if (sessionInfo == null) {
            if (!isTcpProxySupported && value.HasTcpProxy())
                return ChannelProtocol.Tcp; // Tcp is the best replacement when TcpProxy is not supported

            return value;
        }

        // if the server does not support tcp packet (not adapter on server)
        if (!sessionInfo.IsTcpPacketSupported) {
            if (value is ChannelProtocol.Udp && sessionInfo.IsUdpChannelSupported)
                return ChannelProtocol.TcpProxyAndUdp; // server does not support tcp packet, so we have to use Udp

            if (value is ChannelProtocol.Tcp)
                return ChannelProtocol.TcpProxy; // server does not support tcp packet, so we have to use tcp proxy
        }

        // if server does not support tcp proxy (disabled on server for performance)
        if (!sessionInfo.IsTcpProxySupported || !isTcpProxySupported) {
            if (value is ChannelProtocol.TcpProxyAndUdp && sessionInfo.IsUdpChannelSupported)
                return ChannelProtocol.Udp;

            if (value is ChannelProtocol.TcpProxy or ChannelProtocol.TcpProxyAndDropQuic)
                return ChannelProtocol.Tcp;
        }

        return value;
    }
}