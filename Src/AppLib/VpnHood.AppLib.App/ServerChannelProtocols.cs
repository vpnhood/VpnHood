namespace VpnHood.AppLib;

public class ServerChannelProtocols
{
    public required bool Udp { get; init; }
    public required bool Tcp { get; init; }
    public required bool TcpProxyAndUdp { get; init; }
    public required bool TcpProxy { get; init; }
    public required bool TcpProxyAndDropQuick { get; init; }

}