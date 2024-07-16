using VpnHood.Common.Messaging;

namespace VpnHood.Common.Exceptions;

public class SessionException : Exception
{
    public SessionException(SessionResponse sessionResponse)
        : base(sessionResponse.ErrorMessage)
    {
        SessionResponse = sessionResponse;
    }

    public SessionException(SessionErrorCode errorCode, string? message = null) : base(message)
    {
        SessionResponse = new SessionResponse {
            ErrorCode = errorCode,
            ErrorMessage = message
        };
    }

    public SessionResponse SessionResponse { get; }
}