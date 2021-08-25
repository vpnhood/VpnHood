namespace VpnHood.Tunneling.Messaging
{
    public class UdpChannelRequest : RequestBase
    {
        public UdpChannelRequest(uint sessionId, byte[] sessionKey) 
            : base(sessionId, sessionKey)
        {
        }
    }
}
