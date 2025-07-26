using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannelOptions : PacketChannelOptions
{
    public required IClientStream ClientStream { get; init; }
    public int ReadBufferSize { get; init; } = 0xFFFF;

}