using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client.Abstractions;

public interface IConnectionInfo
{
    ClientState ClientState { get; }
    SessionErrorCode ErrorCode { get; }
    ApiError? Error { get; }
    SessionInfo? SessionInfo { get; }
    ISessionStatus? SessionStatus { get; }
}