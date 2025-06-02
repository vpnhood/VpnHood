namespace VpnHood.Core.Tunneling;

public class TunnelOptions
{
    public required int MaxPacketChannelCount { get; init; } = 8;
    public required int PacketQueueCapacity { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public bool UseSpeedometerTimer { get; init; }
}