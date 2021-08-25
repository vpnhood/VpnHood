namespace VpnHood.Tunneling.Messaging
{
    public class RequestBase 
    {
        public uint SessionId { get; set; }
        public byte[] SessionKey { get; set; }

        public RequestBase(uint sessionId, byte[] sessionKey)
        {
            SessionId = sessionId;
            SessionKey = sessionKey ?? throw new System.ArgumentNullException(nameof(sessionKey));
        }
    }
}
