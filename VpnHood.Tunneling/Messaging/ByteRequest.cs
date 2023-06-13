namespace VpnHood.Tunneling.Messaging;

public class ByteRequest : RequestBase
{
    public ByteRequest(string requestId, ulong sessionId, byte[] sessionKey)
        : base(Messaging.RequestCode.Bye, requestId, sessionId, sessionKey)
    {
    }
}