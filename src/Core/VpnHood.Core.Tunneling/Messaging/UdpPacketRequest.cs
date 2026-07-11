using System.Net;

namespace VpnHood.Core.Tunneling.Messaging;

public class UdpPacketRequest()
    : RequestBase(Messaging.RequestCode.TcpPacketChannel)
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}