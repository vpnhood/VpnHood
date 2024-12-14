using System.Net;

namespace VpnHood.Tunneling.Messaging;

public class UdpPacketRequest()
    : RequestBase(Messaging.RequestCode.TcpDatagramChannel)
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}