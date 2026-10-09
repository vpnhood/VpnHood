namespace VpnHood.Net.PacketTransports;

public class PacketTransportOptions
{
    public const int DefaultQueueCapacity = 255;
    public int? QueueCapacity { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public required bool Blocking { get; init; }
}