using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging;

public class TcpDatagramChannelRequest : RequestBase
{
    [JsonConstructor]
    public TcpDatagramChannelRequest(string requestId, ulong sessionId, byte[] sessionKey)
        : base(Messaging.RequestCode.TcpDatagramChannel, requestId, sessionId, sessionKey)
    {
    }
}