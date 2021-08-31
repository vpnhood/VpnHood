using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Common.Exceptions
{
    public class SessionException : Exception
    {
        public SessionException(ResponseBase sessionResponse) : base(sessionResponse.ErrorMessage)
        {
            SessionResponse = sessionResponse;
        }

        public SessionException(SessionErrorCode errorCode, string? message = null) : base(message)
        {
            SessionResponse = new ResponseBase(errorCode) {ErrorMessage = message };
        }

        public ResponseBase SessionResponse { get; }
    }
}