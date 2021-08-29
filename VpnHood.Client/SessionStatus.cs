using VpnHood.Common.Messaging;

namespace VpnHood.Client
{
    public class SessionStatus
    {
        public SessionErrorCode ErrorCode { get; internal set; }
        public AccessUsage? AccessUsage { get; internal set; } 
        public SessionSuppressType SuppressedTo { get; internal set; }
        public SessionSuppressType SuppressedBy { get; internal set; }
        public string? ErrorMessage { get; internal set; }
    }
}
