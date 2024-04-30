using System.Net;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class UdpPacketResponse
    : SessionResponse
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}