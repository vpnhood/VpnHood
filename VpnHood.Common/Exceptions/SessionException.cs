using VpnHood.Common.ApiClients;
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

    public virtual ApiError ToApiError()
    {
        var apiError = new ApiError(this);
        apiError.Data.Add(nameof(SessionResponse.ErrorCode), SessionResponse.ErrorCode.ToString());
        apiError.Data.Add(nameof(SessionResponse.SuppressedBy), SessionResponse.SuppressedBy.ToString());
        return apiError;
    }
}