namespace VpnHood.Tunneling.Messaging;

public class UdpChannelRequest : RequestBase
{
    public UdpChannelRequest(ulong sessionId, byte[] sessionKey)
        : base(sessionId, sessionKey)
    {
    }
}