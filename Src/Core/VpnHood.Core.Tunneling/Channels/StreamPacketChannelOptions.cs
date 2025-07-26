using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions : PacketChannelOptions
{
    public required IClientStream ClientStream { get; init; }
    public TransferBufferSize BufferSize { get; set; } = TunnelDefaults.ClientStreamPacketBufferSize;
}