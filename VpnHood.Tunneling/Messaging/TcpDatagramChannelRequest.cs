using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging;

public class TcpDatagramChannelRequest : RequestBase
{
    [JsonConstructor]
    public TcpDatagramChannelRequest(ulong sessionId, byte[] sessionKey)
        : base(sessionId, sessionKey)
    {
    }
}