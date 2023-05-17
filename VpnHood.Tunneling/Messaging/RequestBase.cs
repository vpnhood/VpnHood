using System;

namespace VpnHood.Tunneling.Messaging;

public class RequestBase
{
    public RequestBase(ulong sessionId, byte[] sessionKey)
    {
        SessionId = sessionId;
        SessionKey = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
    }

    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; }
}