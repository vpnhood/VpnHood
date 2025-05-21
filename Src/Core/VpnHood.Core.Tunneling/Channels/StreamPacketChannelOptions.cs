using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions
{
    public required IClientStream ClientStream { get; init; }
    public required string ChannelId { get; init; }
    public required bool Blocking { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public required TimeSpan? Lifespan { get; init; }
}