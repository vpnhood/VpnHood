namespace VpnHood.Tunneling.Messages
{
    public class SessionRequest 
    {
        public int SessionId { get; set; }
        public string ServerId { get; set; }
        public byte[] EnctyptedSessionId { get; set; } // need version 1.1.243 or more
    }
}
