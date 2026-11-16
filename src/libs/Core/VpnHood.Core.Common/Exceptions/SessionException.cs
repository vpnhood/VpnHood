using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown when a server answers a session's request with an error, or a session ends with one.
/// </summary>
/// <remarks>
/// The first two constructors put the response, as JSON, its error code and its
/// <see cref="Messaging.SessionResponse.SuppressedBy"/> in the exception's <see cref="Exception.Data"/>, so an API error
/// made of it can be turned back into it; the API error's constructor does not.
/// </remarks>
public class SessionException : Exception
{
    /// <summary>Gets the server's response.</summary>
    public SessionResponse SessionResponse { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionException"/> class with a server's response.
    /// </summary>
    /// <param name="sessionResponse">
    /// The response, whose message is the exception's; a response with no message is given its error code's standard
    /// one.
    /// </param>
    public SessionException(SessionResponse sessionResponse)
        : base(sessionResponse.ErrorMessage ?? MessageFromErrorCode(sessionResponse.ErrorCode))
    {
        SessionResponse = sessionResponse;
        SessionResponse.ErrorMessage ??= MessageFromErrorCode(sessionResponse.ErrorCode);

        // ReSharper disable VirtualMemberCallInConstructor
        Data.Add(nameof(SessionResponse), JsonSerializer.Serialize(SessionResponse));
        Data.Add(nameof(SessionResponse.ErrorCode), SessionResponse.ErrorCode.ToString());
        Data.Add(nameof(SessionResponse.SuppressedBy), SessionResponse.SuppressedBy.ToString());
        // ReSharper restore VirtualMemberCallInConstructor
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionException"/> class with an error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message, or <see langword="null"/> for the error code's standard one.</param>
    public SessionException(SessionErrorCode errorCode, string? message = null)
        : this(new SessionResponse {
            ErrorCode = errorCode,
            ErrorMessage = message
        })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionException"/> class from an API error that a
    /// <see cref="SessionException"/> became, with the response its data carries.
    /// </summary>
    /// <param name="apiError">The API error.</param>
    /// <exception cref="ArgumentException">
    /// The API error is not one of a <see cref="SessionException"/>, or carries no response.
    /// </exception>
    /// <exception cref="JsonException">The response in the error's data is not valid JSON.</exception>
    /// <exception cref="FormatException">An endpoint in the response is not valid.</exception>
    public SessionException(ApiError apiError)
        : base(apiError.Message)
    {
        // check is it apiError
        if (!apiError.Is<SessionException>())
            throw new ArgumentException("apiError is not a SessionException", nameof(apiError));

        // check is SessionResponse exists in data
        var json = apiError.Data.GetValueOrDefault(nameof(SessionResponse))
                   ?? throw new ArgumentException("apiError does not contain SessionResponse", nameof(apiError));

        SessionResponse = JsonSerializer.Deserialize<SessionResponse>(json)!;
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
            SessionErrorCode.PremiumLocation => "The location is only available for premium accounts.",
            SessionErrorCode.AdError => "An advertisement error occurred.",
            SessionErrorCode.RewardedAdRejected => "The rewarded ad was rejected.",
            SessionErrorCode.Maintenance => "The system is under maintenance.",
            SessionErrorCode.RedirectHost => "The host is being redirected.",
            SessionErrorCode.UnsupportedClient => "The client is unsupported.",
            SessionErrorCode.UnsupportedServer => "The server is unsupported.",
            _ => null
        };
    }
}