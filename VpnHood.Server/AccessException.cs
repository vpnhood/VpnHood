using System;
using VpnHood.Messages;

namespace VpnHood.Server
{
    public class AccessException : Exception
    {
        public Access Access { get; }
        public ResponseCode ResponseCode => Access.StatusCode switch
        {
            AccessStatusCode.Ok => ResponseCode.Ok,
            AccessStatusCode.Expired => ResponseCode.AccessExpired,
            AccessStatusCode.TrafficOverflow => ResponseCode.AccessTrafficOverflow,
            _ => ResponseCode.GeneralError,
        };

        public AccessException(Access access) : base(access.Message)
        {
            Access = access;
        }

    }
}