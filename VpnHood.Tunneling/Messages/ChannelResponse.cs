
namespace VpnHood.Tunneling.Messages
{
    public class ChannelResponse
    {
        public ResponseCode ResponseCode { get; set; }
        public string ErrorMessage { get; set; }
        public SuppressType SuppressedBy { get; set; }
        public AccessUsage AccessUsage { get; set; }
    }


}
