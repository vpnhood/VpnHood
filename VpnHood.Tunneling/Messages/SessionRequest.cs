namespace VpnHood.Tunneling.Messages
{
    public class SessionRequest 
    {
        public int SessionId { get; set; }
        public byte[] SessionKey { get; set; } // need version 1.1.243 or more
    }
}
