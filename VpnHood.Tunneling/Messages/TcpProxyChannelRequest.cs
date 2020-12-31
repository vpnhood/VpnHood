namespace VpnHood.Tunneling.Messages
{
    public class TcpProxyChannelRequest
    {
        public ulong SessionId { get; set; }
        public string ServerId { get; set; }
        public string DestinationAddress { get; set; }
        public ushort DestinationPort { get; set; }

        public byte[] CipherKey { get; set; }
        public long CipherLength { get; set; }
    }
}
