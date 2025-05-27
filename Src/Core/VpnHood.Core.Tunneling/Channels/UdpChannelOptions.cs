using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannelOptions : PacketChannelOptions
{
    public required IPEndPoint RemoteEndPoint { get; init; }
    public required ulong SessionId { get; init; }
    public required byte[] SessionKey { get; init; }
    public required int ProtocolVersion { get; init; }
    public bool LeaveTransmitterOpen { get; init; }
}