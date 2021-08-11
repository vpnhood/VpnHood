namespace VpnHood.Tunneling.Messages
{
    public class SessionRequest 
    {
        public int SessionId { get; set; }
        public byte[] SessionKey { get; set; } = null!;
    }
}
