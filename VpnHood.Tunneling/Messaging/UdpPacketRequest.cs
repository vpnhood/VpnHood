using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging;

public class UdpPacketRequest : RequestBase
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }

    [JsonConstructor]
    public UdpPacketRequest(string requestId, ulong sessionId, byte[] sessionKey)
        : base(Messaging.RequestCode.TcpDatagramChannel, requestId, sessionId, sessionKey)
    {
    }
}