namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannelOptions : PacketChannelOptions
{
    public required ulong SessionId { get; init; }
    public required byte[] SessionKey { get; init; }
    public required bool LeaveTransmitterOpen { get; init; }
    public required int ProtocolVersion { get; init; }
}