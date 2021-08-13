using System;
using System.Net;
using VpnHood.Tunneling.Messages;

namespace VpnHood.Server
{
    public class SessionException : Exception
    {
        public ResponseCode ResponseCode { get; }
        public AccessUsage? AccessUsage { get; }
        public SuppressType SuppressedBy { get; }
        public IPEndPoint? RedirectServerEndPint { get; }
        public DateTime CreatedTime { get; } = DateTime.Now;

        public SessionException(ResponseCode responseCode, AccessUsage? accessUsage = null, string? message = null)
            : base(message)
        {
            AccessUsage = accessUsage;
            ResponseCode = responseCode;
        }

        public SessionException(IPEndPoint redirectServerEndPint, AccessUsage accessUsage)
            : base("ServerRedirect")
        {
            ResponseCode = ResponseCode.RedirectServer;
            RedirectServerEndPint = redirectServerEndPint ?? throw new ArgumentNullException(nameof(redirectServerEndPint));
            AccessUsage = accessUsage;
        }

        public SessionException(SuppressType suppressedBy, AccessUsage accessUsage)
            : base("Session has been suppressed!")
        {
            ResponseCode = ResponseCode.SessionSuppressedBy;
            SuppressedBy = suppressedBy;
            AccessUsage = accessUsage;
        }
    }
}