namespace VpnHood.Core.Tunneling.Messaging;

public class TcpPacketChannelRequest()
    : RequestBase(Messaging.RequestCode.TcpPacketChannel)
{
    public string? ChannelId { get; init; }
    public IReadOnlyList<string>? ActiveChannelIds { get; init; }

    // the seq of the client's next UDP datagram: the server takes the ones before it as older, so a late one cannot
    // turn the session back to UDP
    public ulong? UdpSendSequenceNumber { get; init; }
}