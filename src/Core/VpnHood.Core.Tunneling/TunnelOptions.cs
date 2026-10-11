namespace VpnHood.Core.Tunneling;

public class TunnelOptions
{
    public required int MaxPacketChannelCount { get; init; } = 8;
    public required int Mtu { get; init; }
}
