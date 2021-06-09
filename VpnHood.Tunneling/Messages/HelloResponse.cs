namespace VpnHood.Tunneling.Messages
{
    public class HelloResponse : BaseResponse
    {
        public int SessionId { get; set; }
        public string ServerId { get; set; }
        public byte[] SessionKey { get; set; }
        public int UdpPort { get; set; }
        public byte[] UdpKey { get; set; }
        public SuppressType SuppressedTo { get; set; }
    }
}
