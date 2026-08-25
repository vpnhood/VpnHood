using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions : PacketChannelOptions
{
    public required IStreamConnection StreamConnection { get; init; }
    public TransferBufferSize BufferSize { get; set; } = TransportDefaults.ConnectionPacketBufferSize;
    public required DateTime RequestTime { get; init; }
}