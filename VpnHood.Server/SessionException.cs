using System;
using VpnHood.Tunneling.Messages;

namespace VpnHood.Server
{
    public class SessionException : Exception
    {
        public AccessUsage AccessUsage { get; }
        public ResponseCode ResponseCode { get; }
        public SuppressType SuppressedBy { get; }
        public DateTime CreatedTime { get; } = DateTime.Now;

        public SessionException(AccessUsage accessUsage, ResponseCode responseCode, SuppressType suppressedBy, string message) : base(message)
        {
            AccessUsage = accessUsage;
            ResponseCode = responseCode;
            SuppressedBy = suppressedBy;
        }

    }
}