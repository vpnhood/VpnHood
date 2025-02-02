using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Common.Exceptions;

public class SessionException : Exception
{
    public SessionException(SessionResponse sessionResponse)
        : base(sessionResponse.ErrorMessage ?? MessageFromErrorCode(sessionResponse.ErrorCode))
    {
        SessionResponse = sessionResponse;
        SessionResponse.ErrorMessage ??= MessageFromErrorCode(sessionResponse.ErrorCode);
        
        // ReSharper disable VirtualMemberCallInConstructor
        Data.Add(nameof(SessionResponse.ErrorCode), SessionResponse.ErrorCode.ToString());
        Data.Add(nameof(SessionResponse.SuppressedBy), SessionResponse.SuppressedBy.ToString());
        Data.Add(nameof(SessionResponse.AccessKey), SessionResponse.AccessKey);
        // ReSharper restore VirtualMemberCallInConstructor
    }

    public SessionException(SessionErrorCode errorCode, string? message = null)
        : this(new SessionResponse {
            ErrorCode = errorCode,
            ErrorMessage = message
        })
    {
    }

    private static string? MessageFromErrorCode(SessionErrorCode errorCode)
    {
        return errorCode switch {
            SessionErrorCode.Ok => "Operation completed successfully.",
            SessionErrorCode.AccessError => "An access error occurred.",
            SessionErrorCode.PlanRejected => "The requested connection plan has been rejected.",
            SessionErrorCode.GeneralError => "A general error occurred.",
            SessionErrorCode.SessionClosed => "The session has been closed.",
            SessionErrorCode.SessionSuppressedBy => "The session was suppressed by another session.",
            SessionErrorCode.SessionError => "An error occurred in the session.",
            SessionErrorCode.SessionExpired => "The session has expired.",
            SessionErrorCode.AccessExpired => "Access has expired.",
            SessionErrorCode.DailyLimitExceeded => "The daily limit has been exceeded.",
            SessionErrorCode.AccessCodeRejected => "The access code was rejected.",
            SessionErrorCode.AccessLocked => "Access is locked.",
            SessionErrorCode.AccessTrafficOverflow => "Access traffic overflow occurred.",
            SessionErrorCode.NoServerAvailable => "No server is available.",
            SessionErrorCode.PremiumLocation=> "The location is only available for premium accounts.",
            SessionErrorCode.AdError => "An advertisement error occurred.",
            SessionErrorCode.RewardedAdRejected => "The rewarded advertisement was rejected.",
            SessionErrorCode.Maintenance => "The system is under maintenance.",
            SessionErrorCode.RedirectHost => "The host is being redirected.",
            SessionErrorCode.UnsupportedClient => "The client is unsupported.",
            SessionErrorCode.UnsupportedServer => "The server is unsupported.",
            _ => null,
        };
    }

    public SessionResponse SessionResponse { get; }
}