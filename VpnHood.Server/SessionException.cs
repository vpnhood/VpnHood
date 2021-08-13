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

        public SessionException(ResponseCode responseCode, SuppressType suppressedBy, AccessUsage? accessUsage, string? message) 
            : base(message)
        {
            AccessUsage = accessUsage;
            ResponseCode = responseCode;
            SuppressedBy = suppressedBy;
        }
    }
}