using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

[method: JsonConstructor]
public class UdpPacketResponse(SessionErrorCode errorCode) : SessionResponse(errorCode)
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}