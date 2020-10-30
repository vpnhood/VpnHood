using System;
using VpnHood.Messages;

namespace VpnHood.Server
{
    public class AccessException : Exception
    {
        public Access Access { get; }
        public HelloResponse.Code HelloResponseCode => Access.StatusCode switch
        {
            AccessStatusCode.Ok => HelloResponse.Code.Ok,
            AccessStatusCode.Expired => HelloResponse.Code.Expired,
            AccessStatusCode.TrafficOverflow => HelloResponse.Code.TrafficOverflow,
            _ => HelloResponse.Code.Error,
        };

        public AccessException(Access access) : base(access.Message)
        {
            Access = access;
        }

    }
}