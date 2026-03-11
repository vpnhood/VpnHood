namespace VpnHood.Core.Tunneling.Messaging;

public class TcpPacketChannelRequest()
    : RequestBase(Messaging.RequestCode.TcpPacketChannel)
{
    public string? ChannelId { get; init; }
    public IReadOnlyList<string>? ActiveChannelIds { get; init; }
}