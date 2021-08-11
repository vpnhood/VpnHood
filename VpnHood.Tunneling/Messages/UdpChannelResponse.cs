namespace VpnHood.Tunneling.Messages
{
    public class UdpChannelResponse : BaseResponse
    {
        public int UdpPort { get; set; }
        public byte[] UdpKey { get; set; } = null!;
    }

}
