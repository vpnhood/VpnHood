namespace VpnHood.Tunneling.Messaging;

public class ByeRequest : RequestBase
{
    public ByeRequest(string requestId, ulong sessionId, byte[] sessionKey)
        : base(Messaging.RequestCode.Bye, requestId, sessionId, sessionKey)
    {
    }
}