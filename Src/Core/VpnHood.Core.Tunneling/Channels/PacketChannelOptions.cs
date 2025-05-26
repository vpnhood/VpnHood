namespace VpnHood.Core.Tunneling.Channels;

public class PacketChannelOptions
{
    public int? QueueCapacity { get; init; } 
    public required bool AutoDisposePackets { get; init; }
    public required bool Blocking { get; init; }
    public required TimeSpan? Lifespan { get; init; }
    public required string ChannelId { get; init; }
}