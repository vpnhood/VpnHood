namespace VpnHood.Tunneling.Messages
{
    public class BaseResponse 
    {
        public ResponseCode ResponseCode { get; set; }
        public AccessUsage AccessUsage { get; set; }
        public string ErrorMessage { get; set; }
        public SuppressType SuppressedBy { get; set; }
    }
}
