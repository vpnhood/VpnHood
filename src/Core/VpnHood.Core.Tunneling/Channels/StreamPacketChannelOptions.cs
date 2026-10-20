using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions
{
    public int? QueueCapacity { get; init; }
    public required bool AutoDisposePackets { get; init; }
    public required bool Blocking { get; init; }
    public required TimeSpan? Lifespan { get; init; }
    public required string ChannelId { get; init; }
    public TrafficMeter? TrafficMeter { get; init; }
    public required IStreamConnection StreamConnection { get; init; }
    public TransferBufferSize BufferSize { get; set; } = TransportDefaults.ConnectionPacketBufferSize;
    public required DateTime RequestTime { get; init; }
}
