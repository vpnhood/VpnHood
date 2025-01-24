using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

// to must be internal
public class SessionStatus
{
    public SessionErrorCode ErrorCode { get; internal set; }
    public ApiError? Error { get; internal set; }
    public SessionSuppressType SuppressedBy { get; private set; }
    public AccessUsage AccessUsage { get; private set; } = new ();
    public string? ClientCountry { get; private set; }
    public string? AccessKey { get; private set; }

    internal void Update(SessionResponse response)
    {
        if (ErrorCode == SessionErrorCode.SessionSuppressedBy)
            SuppressedBy = response.SuppressedBy;

        if (response.AccessUsage != null)
            AccessUsage = response.AccessUsage;

        if (!string.IsNullOrEmpty(response.ClientCountry))
            ClientCountry = response.ClientCountry;

        if (!string.IsNullOrEmpty(response.AccessKey))
            AccessKey = response.AccessKey;
    }

    internal void SetException(Exception ex)
    {
        if (ErrorCode!=SessionErrorCode.Ok)
            return; // already set

        if (ex is SessionException sessionException) {
            ErrorCode = sessionException.SessionResponse.ErrorCode;
            Error = sessionException.ToApiError();
            Update(sessionException.SessionResponse); // must after setting Error
        }
        else {
            ErrorCode = SessionErrorCode.GeneralError;
            Error = new ApiError(ex);
        }
    }
}