namespace VpnHood.Tunneling.Messages
{
    public class TcpDatagramChannelRequest
    {
        public ulong SessionId { get; set; }
        public string ServerId { get; set; }
    }
}
