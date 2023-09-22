using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class UdpPacketResponse : SessionResponseBase
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }

    [JsonConstructor]
    public UdpPacketResponse(SessionErrorCode errorCode)
        : base(errorCode)
    {
    }
}