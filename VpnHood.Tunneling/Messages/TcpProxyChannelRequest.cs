namespace VpnHood.Tunneling.Messages
{

    public class TcpProxyChannelRequest : SessionRequest
    {
        public string DestinationAddress { get; set; }
        public ushort DestinationPort { get; set; }
        public byte[] CipherKey { get; set; }
        public long CipherLength { get; set; }
    }
}
