using System.Net;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

public class UdpPacketResponse
    : SessionResponse
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}