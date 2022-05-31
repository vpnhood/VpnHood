using System;

namespace VpnHood.Tunneling.Messaging;

public class RequestBase
{
    public RequestBase(uint sessionId, byte[] sessionKey)
    {
        SessionId = sessionId;
        SessionKey = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
    }

    public uint SessionId { get; set; }
    public byte[] SessionKey { get; set; }
}