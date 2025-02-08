using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

internal class ClientConnectionInfo : IConnectionInfo
{
    public ClientState ClientState { get; set; } = ClientState.None;
    public SessionErrorCode ErrorCode { get; set; } = SessionErrorCode.Ok;
    public ApiError? Error { get; set; }
    public SessionInfo? SessionInfo { get; set; }
    public ISessionStatus? SessionStatus { get; set; }

    private readonly object _updateLock = new();

    internal void SetException(Exception ex)
    {
        lock (_updateLock) {
            if (ErrorCode != SessionErrorCode.Ok)
                return; // already set

            ErrorCode = ex is SessionException sessionException
                ? sessionException.SessionResponse.ErrorCode
                : SessionErrorCode.GeneralError;

            Error = new ApiError(ex);
        }
    }
}