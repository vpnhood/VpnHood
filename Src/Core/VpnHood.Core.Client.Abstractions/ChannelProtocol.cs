namespace VpnHood.Core.Client.Abstractions;

public enum ChannelProtocol
{
    Udp,
    Tcp,
    TcpProxyAndUdp,
    TcpProxy,
    TcpProxyAndDropQuic
}