namespace VpnHood.Tunneling.Messaging;

public class UdpChannelRequest : RequestBase
{
    public UdpChannelRequest(string requestId, ulong sessionId, byte[] sessionKey)
        : base(Messaging.RequestCode.UdpChannel, requestId, sessionId, sessionKey)
    {
    }
}