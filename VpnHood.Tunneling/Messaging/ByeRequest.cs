namespace VpnHood.Tunneling.Messaging;

public class ByeRequest(string requestId, ulong sessionId, byte[] sessionKey)
    : RequestBase(Messaging.RequestCode.Bye, requestId, sessionId, sessionKey);