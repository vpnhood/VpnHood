namespace VpnHood.Tunneling.Messages
{
    public class HelloResponse
    {
        public ResponseCode ResponseCode { get; set; }
        public ulong SessionId { get; set; }
        public string ServerId { get; set; }
        public SuppressType SuppressedTo { get; set; }
        public AccessUsage AccessUsage{get; set;}
        public string ErrorMessage { get; set; }
    }


}
