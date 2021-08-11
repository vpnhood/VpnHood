namespace VpnHood.Tunneling.Messages
{

    public class TcpProxyChannelRequest : SessionRequest
    {
        public string DestinationAddress { get; set; } = null!;
        public ushort DestinationPort { get; set; }
        public byte[] CipherKey { get; set; } = null!;
        public long CipherLength { get; set; }
    }
}
