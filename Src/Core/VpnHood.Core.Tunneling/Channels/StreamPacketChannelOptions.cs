using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions : PacketChannelOptions
{
    public required IConnection Connection { get; init; }
    public TransferBufferSize BufferSize { get; set; } = TunnelDefaults.ConnectionPacketBufferSize;
    public required DateTime RequestTime { get; init; }
}