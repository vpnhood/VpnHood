using System;
using VpnHood.Client;
using VpnHood.Messages;

namespace VpnHood.Server
{
    public class SessionException : Exception
    {
        public AccessUsage AccessUsage { get; }
        public ResponseCode ResponseCode { get; }
        public SuppressType SuppressedBy { get; }

        public SessionException(AccessUsage accessUsage, ResponseCode responseCode, SuppressType suppressedBy, string message) : base(message)
        {
            AccessUsage = accessUsage;
            ResponseCode = responseCode;
            SuppressedBy = suppressedBy;
        }

    }
}