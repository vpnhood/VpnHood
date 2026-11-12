using System.Net;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents UDP packets in a request of their own, which no client sends and the server does not serve.
/// </summary>
/// <remarks>Its code is <see cref="RequestCode.TcpPacketChannel"/>, not <see cref="RequestCode.UdpPacket"/>.</remarks>
public class UdpPacketRequest()
    : RequestBase(Messaging.RequestCode.TcpPacketChannel)
{
    /// <summary>Gets the packets.</summary>
    public required byte[][] PacketBuffers { get; init; }

    /// <summary>Gets the packets' destination.</summary>
    public required IPAddress DestinationAddress { get; init; }
}