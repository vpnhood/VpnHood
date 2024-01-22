using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging;

[method: JsonConstructor]
public class TcpDatagramChannelRequest(string requestId, ulong sessionId, byte[] sessionKey)
    : RequestBase(Messaging.RequestCode.TcpDatagramChannel, requestId, sessionId, sessionKey);