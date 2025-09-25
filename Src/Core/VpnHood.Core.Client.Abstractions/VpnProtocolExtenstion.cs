namespace VpnHood.Core.Client.Abstractions;

public static class VpnProtocolExtenstion
{
    public static bool HasTcpProxy(this ChannelProtocol channelProtocol) =>
        channelProtocol is 
            ChannelProtocol.TcpProxyAndUdp or 
            ChannelProtocol.TcpProxy or 
            ChannelProtocol.TcpProxyAndDropQuic;

    public static bool HasUdp(this ChannelProtocol channelProtocol) =>
        channelProtocol is 
            ChannelProtocol.Udp or 
            ChannelProtocol.TcpProxyAndUdp;

}