namespace VpnHood.Core.PacketTransports;

public class PacketTransportOptions
{
    public int QueueCapacity { get; set; } = 255;
    public required bool AutoDisposeSentPackets { get; init; }
    public required bool AutoDisposeFailedPackets { get; init; }
    public required bool Blocking { get; init; }
}