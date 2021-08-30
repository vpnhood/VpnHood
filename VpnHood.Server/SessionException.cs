using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Server
{
    public class SessionException : Exception
    {
        public ResponseBase SessionResponse { get; }

        public SessionException(ResponseBase sessionResponse) : base(sessionResponse.ErrorMessage)
            => SessionResponse = sessionResponse;

        public SessionException(SessionErrorCode errorCode, string? message = null) : base(message)
            => SessionResponse = new ResponseBase(errorCode) { ErrorMessage = Message };
    }
}