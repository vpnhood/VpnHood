using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client;

internal static class VpnProtocolValidator
{
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