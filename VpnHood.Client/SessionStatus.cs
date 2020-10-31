using VpnHood.Messages;

namespace VpnHood.Client
{
    public class SessionStatus
    {
        public ResponseCode ResponseCode { get; internal set; } 
        public AccessUsage AccessUsage { get; internal set; }
        public SuppressType SuppressedTo { get; internal set; }
        public SuppressType SuppressedBy { get; internal set; }
        public string ErrorMessage { get; internal set; }
    }
}
