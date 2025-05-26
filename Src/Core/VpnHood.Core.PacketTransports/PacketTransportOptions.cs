namespace VpnHood.Core.PacketTransports;

public class PacketTransportOptions
{
    public int? QueueCapacity { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public required bool Blocking { get; init; }
}