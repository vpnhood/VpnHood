using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging;

[method: JsonConstructor]
public class UdpPacketRequest(string requestId, ulong sessionId, byte[] sessionKey)
    : RequestBase(Messaging.RequestCode.TcpDatagramChannel, requestId, sessionId, sessionKey)
{
    public required byte[][] PacketBuffers { get; init; }
    public required IPAddress DestinationAddress { get; init; }
}