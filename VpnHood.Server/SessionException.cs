using System;
using System.Net;
using VpnHood.Tunneling.Messages;

namespace VpnHood.Server
{
    public class SessionException : Exception
    {
        public ResponseCode ResponseCode { get; }
        public AccessUsage AccessUsage { get; }
        public SuppressType SuppressedBy { get; }
        public IPEndPoint RedirectServerEndPint { get; }
        public DateTime CreatedTime { get; } = DateTime.Now;

        public SessionException(AccessUsage accessUsage,
            ResponseCode responseCode, SuppressType suppressedBy, string message, IPEndPoint redirectServerEndPint = null) : base(message)
        {
            AccessUsage = accessUsage;
            ResponseCode = responseCode;
            SuppressedBy = suppressedBy;
            RedirectServerEndPint = redirectServerEndPint;
        }
    }
}