namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannelOptions : PacketChannelOptions
{
    public bool LeaveUdpTransportOpen { get; init; }
}