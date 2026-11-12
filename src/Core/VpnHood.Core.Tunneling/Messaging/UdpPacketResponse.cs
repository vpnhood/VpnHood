using System.Net;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

internal class UdpPacketResponse
    : SessionResponse
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}